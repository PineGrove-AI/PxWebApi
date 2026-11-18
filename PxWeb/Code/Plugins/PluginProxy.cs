using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PxWeb.Code.Plugins
{
    /// <summary>Forwards requests under a plugin's path to the plugin, and its answers back.</summary>
    public interface IPluginProxy
    {
        /// <summary>
        /// Forwards the request to the plugin and writes the plugin's answer to the response: 400 for a path
        /// that would leave the plugin's URL, 502 if the plugin can't be reached, 504 if it takes too long.
        /// </summary>
        Task ForwardAsync(HttpContext context, Plugin plugin);
    }

    /// <summary>
    /// Only the headers listed here cross in either direction: the client's cookies, credentials and
    /// PxWebApi's admin key never reach the plugin, and the plugin can't set cookies on PxWebApi's origin.
    /// </summary>
    public class PluginProxy : IPluginProxy
    {
        /// <summary>The client's headers that go on to the plugin.</summary>
        private static readonly string[] ForwardedRequestHeaders =
            ["Accept", "Accept-Language", "If-None-Match", "If-Modified-Since"];

        /// <summary>The plugin's headers that go back to the client, besides Content-Type and Content-Length.</summary>
        private static readonly string[] ForwardedResponseHeaders =
            ["Cache-Control", "Content-Language", "ETag", "Expires", "Last-Modified", "Vary"];

        private readonly IHttpClientFactory _clients;
        private readonly ILogger<PluginProxy> _logger;

        public PluginProxy(IHttpClientFactory clients, ILogger<PluginProxy> logger)
        {
            _clients = clients;
            _logger = logger;
        }

        public async Task ForwardAsync(HttpContext context, Plugin plugin)
        {
            var target = plugin.TargetUri(context.Request);
            if (target is null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(plugin.Options.TimeoutSeconds));

            try
            {
                using var request = CreateRequest(context.Request, target, plugin);
                using var response = await _clients.CreateClient(plugin.HttpClientName)
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

                context.Response.StatusCode = (int)response.StatusCode;
                CopyResponseHeaders(response, context.Response);
                await response.Content.CopyToAsync(context.Response.Body, timeout.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
            {
                if (context.RequestAborted.IsCancellationRequested)
                {
                    return; // The client left; there is no one to answer.
                }

                var timedOut = ex is OperationCanceledException;
                _logger.LogPluginFailed(plugin.Name, timedOut ? "timed out" : ex.Message);
                if (context.Response.HasStarted)
                {
                    context.Abort(); // Half an answer must not pass for a whole one.
                    return;
                }
                context.Response.Clear();
                context.Response.StatusCode = timedOut ? StatusCodes.Status504GatewayTimeout : StatusCodes.Status502BadGateway;
            }
        }

        private static HttpRequestMessage CreateRequest(HttpRequest incoming, Uri target, Plugin plugin)
        {
            var request = new HttpRequestMessage(new HttpMethod(incoming.Method), target);

            if (incoming.ContentLength > 0 || incoming.Headers.TransferEncoding.Count > 0)
            {
                request.Content = new StreamContent(incoming.Body);
                if (incoming.ContentType is { } contentType)
                {
                    request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                }
            }

            foreach (var name in ForwardedRequestHeaders)
            {
                if (incoming.Headers.TryGetValue(name, out var values))
                {
                    request.Headers.TryAddWithoutValidation(name, values.ToArray());
                }
            }

            // The plugin's own headers last, so a client can't override them.
            foreach (var header in plugin.Options.Headers)
            {
                request.Headers.Remove(header.Key);
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return request;
        }

        private static void CopyResponseHeaders(HttpResponseMessage from, HttpResponse to)
        {
            if (from.Content.Headers.ContentType is { } contentType)
            {
                to.ContentType = contentType.ToString();
            }
            if (from.Content.Headers.ContentLength is { } length)
            {
                to.ContentLength = length;
            }

            foreach (var name in ForwardedResponseHeaders)
            {
                if (from.Headers.TryGetValues(name, out var values) || from.Content.Headers.TryGetValues(name, out values))
                {
                    to.Headers[name] = values.ToArray();
                }
            }
        }
    }
}
