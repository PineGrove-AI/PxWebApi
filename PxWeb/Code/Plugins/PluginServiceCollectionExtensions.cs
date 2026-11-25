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
    public static class PluginServiceCollectionExtensions
    {
        public const string SectionName = "Plugins";

        public static void AddPxPlugins(this IServiceCollection services, WebApplicationBuilder builder)
        {
            var entries = builder.Configuration.GetSection(SectionName).GetChildren()
                .Where(entry => entry.GetValue("Enabled", true))
                .Select(entry => (entry.Path, entry.Get<PluginOptions>() ?? new PluginOptions()));

            var registry = new PluginRegistry(entries);
            services.AddSingleton(registry);
            services.AddHttpClient();
            services.AddSingleton<IPluginProxy, PluginProxy>();

            foreach (var plugin in registry.Plugins)
            {
                // The proxy applies the plugin's timeout to the whole response, including the body.
                services.AddHttpClient(plugin.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
                    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                    {
                        AllowAutoRedirect = false,
                        AutomaticDecompression = DecompressionMethods.All,
                        UseCookies = false,
                    });
            }
        }

        public static void MapPxPlugins(this WebApplication app)
        {
            var registry = app.Services.GetRequiredService<PluginRegistry>();
            foreach (var plugin in registry.Plugins)
            {
                app.MapMethods($"/{plugin.BasePath}/{{**rest}}", [HttpMethods.Get, HttpMethods.Head, HttpMethods.Post],
                    (HttpContext context, IPluginProxy proxy) => proxy.ForwardAsync(context, plugin))
                    .ExcludeFromDescription();

                app.Logger.LogPluginLoaded(plugin.Name, "/" + plugin.BasePath, plugin.Upstream.GetLeftPart(UriPartial.Path));
            }
        }
    }
}
