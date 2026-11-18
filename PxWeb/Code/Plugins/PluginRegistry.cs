using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Http;

using PxWeb.Api2.Server.Models;

namespace PxWeb.Code.Plugins
{
    public record Plugin(string Name, string BasePath, Uri Upstream, PluginOptions Options)
    {
        public string HttpClientName => $"plugin:{Name}";

        /// <summary>
        /// The upstream URL for a request under the plugin's path, or null if the request is outside it
        /// or its path would leave the plugin's base URL.
        /// </summary>
        public Uri? GetTargetUri(HttpRequest request)
        {
            if (!request.Path.StartsWithSegments("/" + BasePath, out var rest))
            {
                return null;
            }

            if (rest.Value?.Split('/').Any(segment => segment is "." or "..") == true)
            {
                return null;
            }

            var target = new Uri(Upstream, rest.ToUriComponent().TrimStart('/') + request.QueryString.ToUriComponent());
            return target.AbsoluteUri.StartsWith(Upstream.AbsoluteUri, StringComparison.Ordinal) ? target : null;
        }
    }

    public static class PluginFeatureParams
    {
        public const string Plugin = "plugin";
        public const string Path = "path";
        public const string TableSearch = "tableSearch";
        public const string Label = "label";
    }

    public partial class PluginRegistry
    {
        public const string RouteSegment = "plugins";

        private static readonly string[] ReservedParams =
            [PluginFeatureParams.Plugin, PluginFeatureParams.Path, PluginFeatureParams.TableSearch, PluginFeatureParams.Label];

        public PluginRegistry(IEnumerable<(string ConfigPath, PluginOptions Options)> entries)
        {
            var plugins = new List<Plugin>();
            foreach (var (configPath, options) in entries)
            {
                var plugin = CreatePlugin(configPath, options);
                if (plugins.Any(p => p.Name == plugin.Name))
                {
                    throw new InvalidOperationException($"{configPath}: a plugin named '{plugin.Name}' is already configured.");
                }
                plugins.Add(plugin);
            }
            Plugins = plugins;
        }

        public IReadOnlyList<Plugin> Plugins { get; }

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

        private static Plugin CreatePlugin(string configPath, PluginOptions options)
        {
            if (!ValidName().IsMatch(options.Name))
            {
                throw new InvalidOperationException($"{configPath}: Name '{options.Name}' must consist of lowercase letters, digits and dashes.");
            }

            if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var url)
                || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
                || url.Query.Length > 0
                || url.Fragment.Length > 0)
            {
                throw new InvalidOperationException($"{configPath}: Url '{options.Url}' must be an absolute http or https URL without query or fragment.");
            }

            if (options.TimeoutSeconds <= 0)
            {
                throw new InvalidOperationException($"{configPath}: TimeoutSeconds must be greater than 0.");
            }

            if (options.TableSearch is not null
                && (options.TableSearch.Contains("..") || options.TableSearch.Contains('?') || Uri.IsWellFormedUriString(options.TableSearch, UriKind.Absolute)))
            {
                throw new InvalidOperationException($"{configPath}: TableSearch must be a path relative to Url.");
            }

            var reserved = options.Params.Keys.FirstOrDefault(ReservedParams.Contains);
            if (reserved is not null)
            {
                throw new InvalidOperationException($"{configPath}: Params cannot contain '{reserved}'.");
            }

            // Treat the URL as a directory so the rest of the request path is appended to it.
            var upstream = url.AbsolutePath.EndsWith('/') ? url : new Uri(url.AbsoluteUri + "/");
            return new Plugin(options.Name, $"{RouteSegment}/{options.Name}", upstream, options);
        }

        [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
        private static partial Regex ValidName();
    }
}
