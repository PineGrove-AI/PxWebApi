namespace PxWeb.Code.Plugins
{
    public class PluginOptions
    {
        /// <summary>
        /// Route segment under plugins/ and plugin id in GET /config. Lowercase letters, digits and dashes.
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
        /// Name for clients to show, by language id.
        /// </summary>
        public Dictionary<string, string> Labels { get; set; } = [];

        public List<PluginCapabilityOptions> Capabilities { get; set; } = [];
    }

    public class PluginCapabilityOptions
    {
        /// <summary>
        /// Type of capability, for example tableSearch.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Path of the capability's endpoint, relative to Url.
        /// </summary>
        public string Path { get; set; } = string.Empty;
    }
}
