using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace PxWeb.Code.Plugins
{
    /// <summary>
    /// The load context of one plugin assembly. It resolves the plugin's own dependencies from the
    /// plugin's folder (its .deps.json), but leaves every assembly PxWebApi has to the default context,
    /// so the plugin and PxWebApi share them: PxWeb.Plugins.Abstractions in particular, without which the
    /// plugin's <see cref="PxWeb.Plugins.IPxWebApiPlugin"/> would be a different type from PxWebApi's.
    /// </summary>
    public class PluginLoadContext : AssemblyLoadContext
    {
        private static readonly HashSet<string> HostAssemblies = ReadHostAssemblies();

        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath) : base(Path.GetFileNameWithoutExtension(pluginPath))
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is not null && HostAssemblies.Contains(assemblyName.Name))
            {
                return null;
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }

        /// <summary>
        /// The assemblies the default context can load: the shared frameworks and PxWebApi's own
        /// dependencies, which the host lists as trusted platform assemblies.
        /// </summary>
        private static HashSet<string> ReadHostAssemblies()
        {
            var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
            return tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }
}
