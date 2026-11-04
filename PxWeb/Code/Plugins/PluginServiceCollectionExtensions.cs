using System.IO;
using System.Linq;
using System.Reflection;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using PxWeb.Plugins;

namespace PxWeb.Code.Plugins
{
    /// <summary>
    /// Loads the plugins listed under <c>Plugins</c> in appsettings.json and wires them in:
    /// <code>
    /// "Plugins": [
    ///   {
    ///     "Assembly": "plugins/MyPlugin/MyPlugin.dll",
    ///     "Enabled": true,
    ///     "Options": { "AnySetting": "the plugin's own" }
    ///   }
    /// ]
    /// </code>
    /// <c>Assembly</c> is relative to the content root, or absolute. <c>Enabled</c> defaults to true.
    /// No <c>Plugins</c> section means no plugins, and PxWebApi behaves as without this feature.
    /// </summary>
    public static class PluginServiceCollectionExtensions
    {
        public const string SectionName = "Plugins";

        public static void AddPxPlugins(this IServiceCollection services, WebApplicationBuilder builder)
        {
            var plugins = LoadPlugins(builder.Configuration.GetSection(SectionName), builder.Environment.ContentRootPath, services);
            services.AddSingleton(new PluginRegistry(plugins));
        }

        /// <summary>Maps each plugin's endpoints under <c>plugins/{Name}</c>.</summary>
        public static void MapPxPlugins(this WebApplication app)
        {
            var registry = app.Services.GetRequiredService<PluginRegistry>();
            foreach (var loaded in registry.Plugins)
            {
                loaded.Plugin.MapEndpoints(app.MapGroup("/" + loaded.BasePath));
                app.Logger.LogPluginLoaded(loaded.Plugin.Name, loaded.Plugin.GetType().Assembly.Location, "/" + loaded.BasePath);
            }
        }

        /// <summary>
        /// Loads every enabled plugin in the section and lets it register its services. A plugin that
        /// can't be loaded stops startup: a deployment that asked for it would otherwise run without it.
        /// </summary>
        internal static List<IPxWebApiPlugin> LoadPlugins(IConfigurationSection section, string contentRoot, IServiceCollection services)
        {
            var plugins = new List<IPxWebApiPlugin>();
            foreach (var entry in section.GetChildren())
            {
                if (!entry.GetValue("Enabled", true))
                {
                    continue;
                }

                var assemblyPath = entry["Assembly"];
                if (string.IsNullOrWhiteSpace(assemblyPath))
                {
                    throw new InvalidOperationException($"{entry.Path}: Assembly is not set.");
                }

                var fullPath = Path.GetFullPath(assemblyPath, contentRoot);
                if (!File.Exists(fullPath))
                {
                    throw new InvalidOperationException($"{entry.Path}: plugin assembly {fullPath} does not exist.");
                }

                var plugin = CreatePlugin(new PluginLoadContext(fullPath).LoadFromAssemblyPath(fullPath), entry.Path);
                plugin.ConfigureServices(services, entry.GetSection("Options"));
                plugins.Add(plugin);
            }
            return plugins;
        }

        private static IPxWebApiPlugin CreatePlugin(Assembly assembly, string configPath)
        {
            var types = assembly.GetExportedTypes()
                .Where(t => typeof(IPxWebApiPlugin).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
                .ToList();

            if (types.Count != 1)
            {
                throw new InvalidOperationException(
                    $"{configPath}: {assembly.GetName().Name} must have exactly one public class implementing {nameof(IPxWebApiPlugin)}, found {types.Count}.");
            }

            return (IPxWebApiPlugin)(Activator.CreateInstance(types[0])
                ?? throw new InvalidOperationException($"{configPath}: could not create {types[0].FullName}."));
        }
    }
}
