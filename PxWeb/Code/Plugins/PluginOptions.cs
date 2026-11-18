namespace PxWeb.Code.Plugins
{
    /// <summary>
    /// One entry under <c>Plugins</c> in appsettings.json: a service that runs on its own, somewhere
    /// PxWebApi can reach it, and that PxWebApi offers to its clients under <c>plugins/{Name}</c>.
    /// PxWebApi runs none of the plugin's code: it lists the plugin in <c>GET /config</c> and forwards
    /// requests under <c>plugins/{Name}</c> to <see cref="Url"/>.
    /// </summary>
    public class PluginOptions
    {
        /// <summary>
        /// The plugin's id: its route segment under <c>plugins/</c> and its feature id in <c>GET /config</c>.
        /// Lowercase letters, digits and dashes.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Where the plugin is: an absolute http(s) URL that requests under <c>plugins/{Name}/</c> are
        /// forwarded to, e.g. <c>http://ai-search:8080/pxweb/</c>. The rest of the request's path and its
        /// query string are added to it.
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>Optional, defaults to <c>true</c>. A disabled plugin is neither listed nor forwarded to.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>How long PxWebApi waits for the plugin before it answers 504.</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Headers PxWebApi adds to every request it forwards, e.g. the plugin's API key. They stay between
        /// PxWebApi and the plugin: clients never see them.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = [];

        /// <summary>A short name for the plugin's function for clients to show, e.g. on a search toggle.</summary>
        public string? Label { get; set; }

        /// <summary>
        /// The path of the plugin's table search endpoint, relative to <see cref="Url"/> (e.g. <c>search</c>),
        /// if it has one. PxWeb offers it as an alternative to its keyword search.
        /// </summary>
        public string? TableSearch { get; set; }

        /// <summary>
        /// Further parameters for the plugin's feature in <c>GET /config</c>, as they are. For clients that
        /// know them; PxWebApi doesn't read them.
        /// </summary>
        public Dictionary<string, string> Params { get; set; } = [];
    }
}
