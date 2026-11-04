namespace PxWeb.Plugins
{
    /// <summary>
    /// Parameter keys of a plugin's feature in <c>GET /config</c>. Paths are relative to the API root.
    /// </summary>
    public static class PluginFeatureParams
    {
        /// <summary>Always <c>true</c>: marks the feature as a plugin. Added by PxWebApi.</summary>
        public const string Plugin = "plugin";

        /// <summary>The plugin's path, <c>plugins/{Name}</c>. Added by PxWebApi.</summary>
        public const string Path = "path";

        /// <summary>
        /// The path of a table search endpoint the plugin provides (see <see cref="TableSearchResponse"/>).
        /// PxWeb offers it as an alternative to its keyword search.
        /// </summary>
        public const string TableSearch = "tableSearch";

        /// <summary>A short name for the plugin's function for clients to show, e.g. on a search toggle.</summary>
        public const string Label = "label";
    }
}
