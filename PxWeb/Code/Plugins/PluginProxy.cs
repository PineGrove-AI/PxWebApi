using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PxWeb.Code.Plugins
{
    public interface IPluginProxy
    {
        Task ForwardAsync(HttpContext context, Plugin plugin);
    }

    /// <summary>
    /// Forwards requests to plugins. Only the headers listed below are passed on in either direction,
    /// so client cookies and credentials never reach the plugin and the plugin cannot set cookies.
    /// </summary>
    public class PluginProxy : IPluginProxy
    {
        private static readonly string[] RequestHeaders = ["Accept", "Accept-Language", "If-None-Match", "If-Modified-Since"];
        private static readonly string[] ResponseHeaders = ["Cache-Control", "Content-Language", "ETag", "Expires", "Last-Modified", "Vary"];

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<PluginProxy> _logger;

        public PluginProxy(IHttpClientFactory httpClientFactory, ILogger<PluginProxy> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task ForwardAsync(HttpContext context, Plugin plugin)
        {
            var target = plugin.GetTargetUri(context.Request);
            if (target is null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            cts.CancelAfter(TimeSpan.FromSeconds(plugin.Options.TimeoutSeconds));

            try
            {
                using var request = CreateRequest(context.Request, target, plugin);
                using var response = await _httpClientFactory.CreateClient(plugin.HttpClientName)
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                context.Response.StatusCode = (int)response.StatusCode;
                CopyResponseHeaders(response, context.Response);
                await response.Content.CopyToAsync(context.Response.Body, cts.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
            {
                if (context.RequestAborted.IsCancellationRequested)
                {
                    return;
                }

                var timedOut = ex is OperationCanceledException;
                _logger.LogPluginFailed(plugin.Name, timedOut ? "timed out" : ex.Message);

                if (context.Response.HasStarted)
                {
                    context.Abort();
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
                if (incoming.ContentType is not null)
                {
                    request.Content.Headers.TryAddWithoutValidation("Content-Type", incoming.ContentType);
                }
            }

            foreach (var name in RequestHeaders)
            {
                if (incoming.Headers.TryGetValue(name, out var values))
                {
                    request.Headers.TryAddWithoutValidation(name, values.ToArray());
                }
            }

            foreach (var header in plugin.Options.Headers)
            {
                request.Headers.Remove(header.Key);
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return request;
        }

        private static void CopyResponseHeaders(HttpResponseMessage from, HttpResponse to)
        {
            if (from.Content.Headers.ContentType is not null)
            {
                to.ContentType = from.Content.Headers.ContentType.ToString();
            }

            if (from.Content.Headers.ContentLength is not null)
            {
                to.ContentLength = from.Content.Headers.ContentLength;
            }

            foreach (var name in ResponseHeaders)
            {
                if (from.Headers.TryGetValues(name, out var values) || from.Content.Headers.TryGetValues(name, out values))
                {
                    to.Headers[name] = values.ToArray();
                }
            }
        }
    }
}
