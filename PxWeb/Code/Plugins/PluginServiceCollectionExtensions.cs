using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PxWeb.Code.Plugins
{
    /// <summary>
    /// Reads the plugins listed under <c>Plugins</c> in appsettings.json and offers them:
    /// <code>
    /// "Plugins": [
    ///   {
    ///     "Name": "ai-search",
    ///     "Url": "http://ai-search:8080/pxweb/",
    ///     "Headers": { "Authorization": "Bearer ..." },
    ///     "Label": "AI search",
    ///     "TableSearch": "search"
    ///   }
    /// ]
    /// </code>
    /// A plugin is a service of its own. PxWebApi lists it in <c>GET /config</c> and forwards requests under
    /// <c>plugins/{Name}/</c> to its <c>Url</c>; it loads no code. No <c>Plugins</c> section means no
    /// plugins, and PxWebApi behaves as without this feature.
    /// </summary>
    public static class PluginServiceCollectionExtensions
    {
        public const string SectionName = "Plugins";

        public static void AddPxPlugins(this IServiceCollection services, WebApplicationBuilder builder)
        {
            var registry = ReadPlugins(builder.Configuration.GetSection(SectionName));
            services.AddSingleton(registry);
            services.AddHttpClient(); // The proxy's IHttpClientFactory, also when no plugin is configured.
            services.AddSingleton<IPluginProxy, PluginProxy>();

            foreach (var plugin in registry.Plugins)
            {
                // The proxy's own timeout covers the whole answer, body included, so the client has none.
                // No redirects: the plugin's URL is the one configured. Compressed answers are decompressed,
                // since Content-Encoding isn't passed on.
                services.AddHttpClient(plugin.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
                    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                    {
                        AllowAutoRedirect = false,
                        AutomaticDecompression = DecompressionMethods.All,
                        UseCookies = false,
                    });
            }
        }

        /// <summary>Forwards each plugin's path, <c>plugins/{Name}/</c>, to the plugin.</summary>
        public static void MapPxPlugins(this WebApplication app)
        {
            var registry = app.Services.GetRequiredService<PluginRegistry>();
            foreach (var plugin in registry.Plugins)
            {
                app.MapMethods("/" + plugin.BasePath + "/{**rest}", [HttpMethods.Get, HttpMethods.Head, HttpMethods.Post],
                    (HttpContext context, IPluginProxy proxy) => proxy.ForwardAsync(context, plugin))
                    .ExcludeFromDescription();
                app.Logger.LogPluginLoaded(plugin.Name, "/" + plugin.BasePath, plugin.Upstream.GetLeftPart(UriPartial.Path));
            }
        }

        /// <summary>The enabled entries in the section, checked: one that is wrong stops startup.</summary>
        internal static PluginRegistry ReadPlugins(IConfigurationSection section) =>
            new(section.GetChildren()
                .Where(entry => entry.GetValue("Enabled", true))
                .Select(entry => (entry.Path, entry.Get<PluginOptions>() ?? new PluginOptions())));
    }
}
