using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Http;

using PxWeb.Api2.Server.Models;

namespace PxWeb.Code.Plugins
{
    /// <summary>A plugin PxWebApi offers, and where.</summary>
    /// <param name="Name">The plugin's id.</param>
    /// <param name="BasePath">Its path relative to the API root: <c>plugins/{Name}</c>.</param>
    /// <param name="Upstream">Where requests under <see cref="BasePath"/> are forwarded to. Ends with a slash.</param>
    /// <param name="Options">Its entry in appsettings.json.</param>
    public record Plugin(string Name, string BasePath, Uri Upstream, PluginOptions Options)
    {
        /// <summary>The named <see cref="System.Net.Http.HttpClient"/> that forwards to this plugin.</summary>
        public string HttpClientName => $"plugin:{Name}";

        /// <summary>
        /// The plugin's URL for a request under its path: the rest of the path under <c>plugins/{Name}/</c>,
        /// and the query string, added to <see cref="Upstream"/>. Null for a request outside the plugin's
        /// path, or one whose path would leave <see cref="Upstream"/>.
        /// </summary>
        public Uri? TargetUri(HttpRequest request)
        {
            if (!request.Path.StartsWithSegments("/" + BasePath, out var rest))
            {
                return null;
            }
            if (rest.Value?.Split('/').Any(segment => segment is "." or "..") == true)
            {
                return null;
            }

            var relative = rest.ToUriComponent().TrimStart('/') + request.QueryString.ToUriComponent();
            var target = new Uri(Upstream, relative);
            return target.AbsoluteUri.StartsWith(Upstream.AbsoluteUri, StringComparison.Ordinal) ? target : null;
        }
    }

    /// <summary>
    /// Parameter keys of a plugin's feature in <c>GET /config</c>. Paths are relative to the API root.
    /// </summary>
    public static class PluginFeatureParams
    {
        /// <summary>Always <c>true</c>: marks the feature as a plugin.</summary>
        public const string Plugin = "plugin";

        /// <summary>The plugin's path, <c>plugins/{Name}</c>.</summary>
        public const string Path = "path";

        /// <summary>The path of the plugin's table search endpoint, which PxWeb offers next to its keyword search.</summary>
        public const string TableSearch = "tableSearch";

        /// <summary>A short name for the plugin's function for clients to show.</summary>
        public const string Label = "label";
    }

    /// <summary>The plugins PxWebApi offers. Empty when none are configured.</summary>
    public partial class PluginRegistry
    {
        public const string RouteSegment = "plugins";

        private static readonly HashSet<string> ReservedParams =
            [PluginFeatureParams.Plugin, PluginFeatureParams.Path, PluginFeatureParams.TableSearch, PluginFeatureParams.Label];

        /// <param name="entries">The enabled entries under <c>Plugins</c>, with their configuration paths
        /// (e.g. <c>Plugins:0</c>) for error messages.</param>
        public PluginRegistry(IEnumerable<(string ConfigPath, PluginOptions Options)> entries)
        {
            var plugins = new List<Plugin>();
            foreach (var (configPath, options) in entries)
            {
                var plugin = Validate(configPath, options);
                if (plugins.Any(p => p.Name == plugin.Name))
                {
                    throw new InvalidOperationException($"{configPath}: two plugins have the name \"{plugin.Name}\".");
                }
                plugins.Add(plugin);
            }
            Plugins = plugins;
        }

        public IReadOnlyList<Plugin> Plugins { get; }

        /// <summary>One feature per plugin, for <c>GET /config</c>.</summary>
        public IEnumerable<ApiFeature> GetFeatures()
        {
            foreach (var plugin in Plugins)
            {
                var feature = new ApiFeature
                {
                    Id = plugin.Name,
                    Params =
                    [
                        new() { Key = PluginFeatureParams.Plugin, Value = "true" },
                        new() { Key = PluginFeatureParams.Path, Value = plugin.BasePath },
                    ],
                };
                if (!string.IsNullOrWhiteSpace(plugin.Options.TableSearch))
                {
                    feature.Params.Add(new() { Key = PluginFeatureParams.TableSearch, Value = $"{plugin.BasePath}/{plugin.Options.TableSearch.TrimStart('/')}" });
                }
                if (!string.IsNullOrWhiteSpace(plugin.Options.Label))
                {
                    feature.Params.Add(new() { Key = PluginFeatureParams.Label, Value = plugin.Options.Label });
                }
                foreach (var param in plugin.Options.Params)
                {
                    feature.Params.Add(new() { Key = param.Key, Value = param.Value });
                }
                yield return feature;
            }
        }

        /// <summary>
        /// Checks an entry, so that a deployment that asked for a plugin doesn't start without it, or with
        /// one that can't work. Nothing here contacts the plugin: it may well start after PxWebApi.
        /// </summary>
        private static Plugin Validate(string configPath, PluginOptions options)
        {
            if (!ValidName().IsMatch(options.Name))
            {
                throw new InvalidOperationException(
                    $"{configPath}: Name is \"{options.Name}\": use lowercase letters, digits and dashes.");
            }

            if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException($"{configPath}: Url \"{options.Url}\" is not an absolute http or https URL.");
            }
            if (!string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment))
            {
                throw new InvalidOperationException($"{configPath}: Url must not have a query or fragment.");
            }

            if (options.TimeoutSeconds <= 0)
            {
                throw new InvalidOperationException($"{configPath}: TimeoutSeconds must be more than 0.");
            }

            if (options.TableSearch is { } tableSearch && (tableSearch.Contains("..") || tableSearch.Contains('?') || Uri.TryCreate(tableSearch, UriKind.Absolute, out _)))
            {
                throw new InvalidOperationException($"{configPath}: TableSearch must be a path relative to Url, e.g. \"search\".");
            }

            var reserved = options.Params.Keys.FirstOrDefault(ReservedParams.Contains);
            if (reserved is not null)
            {
                throw new InvalidOperationException($"{configPath}: Params can't set \"{reserved}\": PxWebApi sets it.");
            }

            // A trailing slash makes the URL a folder, so the rest of the request's path goes under it.
            var upstream = url.AbsolutePath.EndsWith('/') ? url : new Uri(url.AbsoluteUri + "/");
            return new Plugin(options.Name, $"{RouteSegment}/{options.Name}", upstream, options);
        }

        [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
        private static partial Regex ValidName();
    }
}
