namespace PxWeb.Code.Plugins
{
    public class PluginOptions
    {
        /// <summary>
        /// Route segment under plugins/ and feature id in GET /config. Lowercase letters, digits and dashes.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Base URL of the plugin service. Requests to plugins/{Name}/{path} are forwarded to {Url}/{path}.
        /// </summary>
        public string Url { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;

        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Headers added to every forwarded request, for example an API key for the plugin.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = [];

        /// <summary>
        /// Name for clients to show, for example on a search mode toggle.
        /// </summary>
        public string? Label { get; set; }

        /// <summary>
        /// Path of the plugin's table search endpoint, relative to Url.
        /// </summary>
        public string? TableSearch { get; set; }

        /// <summary>
        /// Additional parameters for the plugin's feature in GET /config.
        /// </summary>
        public Dictionary<string, string> Params { get; set; } = [];
    }
}
