using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PxWeb.Plugins.Sample
{
    /// <summary>
    /// A table search plugin over a fixed list of tables: its <c>Options</c> name them, and a search
    /// returns those whose title contains every word of the query. Configured as:
    /// <code>
    /// "Plugins": [
    ///   {
    ///     "Assembly": "plugins/PxWeb.Plugins.Sample/PxWeb.Plugins.Sample.dll",
    ///     "Options": {
    ///       "Label": "Sample search",
    ///       "Tables": [ { "Id": "TAB638", "Title": "Population by region" } ]
    ///     }
    ///   }
    /// ]
    /// </code>
    /// </summary>
    public class SamplePlugin : IPxWebApiPlugin
    {
        private SampleOptions _options = new();

        public string Name => "sample";

        public void ConfigureServices(IServiceCollection services, IConfiguration options)
        {
            _options = options.Get<SampleOptions>() ?? new SampleOptions();
            services.AddSingleton(_options);
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("search", (string? query, string? lang, SampleOptions options) =>
            {
                var words = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var tables = options.Tables
                    .Where(t => words.Length > 0 && words.All(w => t.Title.Contains(w, StringComparison.OrdinalIgnoreCase)))
                    .Select(t => new TableSearchHit { Id = t.Id })
                    .ToList();
                return Results.Ok(new TableSearchResponse { Tables = tables, Note = $"{tables.Count} of {options.Tables.Count} tables" });
            });
        }

        public IEnumerable<KeyValuePair<string, string>> GetFeatureParams(string basePath) =>
        [
            new(PluginFeatureParams.TableSearch, basePath + "/search"),
            new(PluginFeatureParams.Label, _options.Label),
        ];
    }

    public class SampleOptions
    {
        public string Label { get; set; } = "Sample search";
        public List<SampleTable> Tables { get; set; } = [];
    }

    public class SampleTable
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }
}
