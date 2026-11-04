using System.Linq;
using System.Text.RegularExpressions;

using PxWeb.Api2.Server.Models;
using PxWeb.Plugins;

namespace PxWeb.Code.Plugins
{
    /// <summary>A plugin PxWebApi runs, and where its endpoints are.</summary>
    /// <param name="Plugin">The plugin.</param>
    /// <param name="BasePath">Its path relative to the API root: <c>plugins/{Name}</c>.</param>
    public record LoadedPlugin(IPxWebApiPlugin Plugin, string BasePath);

    /// <summary>The plugins PxWebApi runs. Empty when none are configured.</summary>
    public partial class PluginRegistry
    {
        public const string RouteSegment = "plugins";

        public PluginRegistry(IEnumerable<IPxWebApiPlugin> plugins)
        {
            var loaded = new List<LoadedPlugin>();
            foreach (var plugin in plugins)
            {
                if (!ValidName().IsMatch(plugin.Name))
                {
                    throw new InvalidOperationException(
                        $"Plugin {plugin.GetType().FullName} has the name \"{plugin.Name}\": use lowercase letters, digits and dashes.");
                }
                if (loaded.Any(p => p.Plugin.Name == plugin.Name))
                {
                    throw new InvalidOperationException($"Two plugins have the name \"{plugin.Name}\".");
                }
                loaded.Add(new LoadedPlugin(plugin, $"{RouteSegment}/{plugin.Name}"));
            }
            Plugins = loaded;
        }

        public IReadOnlyList<LoadedPlugin> Plugins { get; }

        /// <summary>One feature per plugin, for <c>GET /config</c>.</summary>
        public IEnumerable<ApiFeature> GetFeatures()
        {
            foreach (var loaded in Plugins)
            {
                var feature = new ApiFeature
                {
                    Id = loaded.Plugin.Name,
                    Params =
                    [
                        new() { Key = PluginFeatureParams.Plugin, Value = "true" },
                        new() { Key = PluginFeatureParams.Path, Value = loaded.BasePath },
                    ],
                };
                foreach (var param in loaded.Plugin.GetFeatureParams(loaded.BasePath))
                {
                    feature.Params.Add(new() { Key = param.Key, Value = param.Value });
                }
                yield return feature;
            }
        }

        [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
        private static partial Regex ValidName();
    }
}
