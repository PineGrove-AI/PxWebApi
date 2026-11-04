using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PxWeb.Plugins
{
    /// <summary>
    /// A PxWebApi plugin: an assembly PxWebApi loads at startup when it is listed under <c>Plugins</c> in
    /// appsettings.json. A plugin adds services and endpoints of its own. Its endpoints are mapped under
    /// <c>{RoutePrefix}/plugins/{Name}</c>, and the plugin is listed as a feature in <c>GET /config</c>,
    /// which is how clients find out that it is there.
    /// </summary>
    /// <remarks>
    /// The plugin's assembly is loaded in a load context of its own, so it can bring its own dependencies.
    /// Assemblies PxWebApi already has (this one, ASP.NET Core, and PxWebApi's own dependencies) are
    /// shared with PxWebApi instead, so types such as this interface are the same on both sides.
    /// A plugin needs a public parameterless constructor.
    /// </remarks>
    public interface IPxWebApiPlugin
    {
        /// <summary>
        /// The plugin's id: its route segment under <c>plugins/</c> and its feature id in <c>GET /config</c>.
        /// Lowercase letters, digits and dashes.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Registers the plugin's services. Called once, before the application is built.
        /// </summary>
        /// <param name="services">PxWebApi's service collection.</param>
        /// <param name="options">The plugin's own settings: its <c>Options</c> section in appsettings.json.</param>
        void ConfigureServices(IServiceCollection services, IConfiguration options);

        /// <summary>
        /// Maps the plugin's endpoints. Called once, after the application is built.
        /// </summary>
        /// <param name="endpoints">A route group at <c>plugins/{Name}</c>: routes mapped here are relative to it.</param>
        void MapEndpoints(IEndpointRouteBuilder endpoints);

        /// <summary>
        /// Extra parameters for the plugin's feature in <c>GET /config</c>, next to the ones PxWebApi adds
        /// itself (<see cref="PluginFeatureParams.Plugin"/> and <see cref="PluginFeatureParams.Path"/>).
        /// See <see cref="PluginFeatureParams"/> for the ones clients know.
        /// </summary>
        /// <param name="basePath">The plugin's path relative to the API root, e.g. <c>plugins/my-plugin</c>.</param>
        IEnumerable<KeyValuePair<string, string>> GetFeatureParams(string basePath) => [];
    }
}
