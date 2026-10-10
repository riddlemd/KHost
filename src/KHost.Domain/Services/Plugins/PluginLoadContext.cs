using System.Reflection;
using System.Runtime.Loader;

namespace KHost.Domain.Services.Plugins;

/// <summary>One non-collectible context per plugin: reload happens by restarting the app.</summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginId, string entryAssemblyPath)
        : base($"plugin:{pluginId}", isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Anything the host has loaded (the Sdk above all) must come from the default
        // context: a plugin-local copy would break type identity across the boundary.
        if (Default.Assemblies.Any(a => a.GetName().Name == assemblyName.Name))
            return null;

        // Before the host happens to load it too: the options a plugin reads its settings through are
        // only the host's IOptionsMonitor if both sides share one Microsoft.Extensions.Options.
        if (assemblyName.Name?.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) == true)
            return null;

        // Plugin-private dependency from its own folder; null falls through to the default
        // context's normal resolution (framework assemblies).
        var path = _resolver.ResolveAssemblyToPath(assemblyName);

        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);

        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }
}
