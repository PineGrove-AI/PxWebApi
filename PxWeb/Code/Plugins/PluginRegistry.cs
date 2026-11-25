using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Http;

using PxWeb.Api2.Server.Models;

namespace PxWeb.Code.Plugins
{
    public record ConfiguredPlugin(string Name, string BasePath, Uri Upstream, PluginOptions Options)
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

    public partial class PluginRegistry
    {
        public const string RouteSegment = "plugins";

        public PluginRegistry(IEnumerable<(string ConfigPath, PluginOptions Options)> entries)
        {
            var plugins = new List<ConfiguredPlugin>();
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

        public IReadOnlyList<ConfiguredPlugin> Plugins { get; }

        public List<Plugin> GetPlugins()
        {
            return Plugins.Select(plugin => new Plugin
            {
                Id = plugin.Name,
                Path = plugin.BasePath,
                Labels = plugin.Options.Labels.Select(label => new PluginLabel { Language = label.Key, Text = label.Value }).ToList(),
                Capabilities = plugin.Options.Capabilities.Select(capability => new PluginCapability
                {
                    Type = capability.Type,
                    Path = $"{plugin.BasePath}/{capability.Path.TrimStart('/')}".TrimEnd('/'),
                }).ToList(),
            }).ToList();
        }

        private static ConfiguredPlugin CreatePlugin(string configPath, PluginOptions options)
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

            foreach (var capability in options.Capabilities)
            {
                if (!ValidCapabilityType().IsMatch(capability.Type))
                {
                    throw new InvalidOperationException($"{configPath}: capability type '{capability.Type}' must be a camelCase name, for example tableSearch.");
                }

                if (capability.Path.Contains("..") || capability.Path.Contains('?') || Uri.IsWellFormedUriString(capability.Path, UriKind.Absolute))
                {
                    throw new InvalidOperationException($"{configPath}: the path of capability '{capability.Type}' must be relative to Url.");
                }
            }

            var duplicate = options.Capabilities.GroupBy(c => c.Type).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
            {
                throw new InvalidOperationException($"{configPath}: capability '{duplicate.Key}' is configured more than once.");
            }

            // Treat the URL as a directory so the rest of the request path is appended to it.
            var upstream = url.AbsolutePath.EndsWith('/') ? url : new Uri(url.AbsoluteUri + "/");
            return new ConfiguredPlugin(options.Name, $"{RouteSegment}/{options.Name}", upstream, options);
        }

        [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
        private static partial Regex ValidName();

        [GeneratedRegex("^[a-z][a-zA-Z0-9]*$")]
        private static partial Regex ValidCapabilityType();
    }
}
