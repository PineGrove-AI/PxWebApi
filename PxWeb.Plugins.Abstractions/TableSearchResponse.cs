namespace PxWeb.Plugins
{
    /// <summary>
    /// The response of a plugin's table search endpoint (<see cref="PluginFeatureParams.TableSearch"/>):
    /// <c>GET {tableSearch}?query={text}&amp;lang={language}</c>. Clients treat any other response, or an
    /// error, as "no answer" and fall back to the API's own keyword search.
    /// </summary>
    public sealed class TableSearchResponse
    {
        /// <summary>The matching tables, best match first.</summary>
        public List<TableSearchHit> Tables { get; set; } = [];

        /// <summary>An optional short text about the search for clients to show with the results.</summary>
        public string? Note { get; set; }
    }

    /// <summary>One table in a <see cref="TableSearchResponse"/>.</summary>
    public sealed class TableSearchHit
    {
        /// <summary>The table's id, as in <c>GET /tables</c>.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>An optional relevance score; higher is better.</summary>
        public double? Score { get; set; }
    }
}
