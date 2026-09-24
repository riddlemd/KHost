using KHost.Abstractions.Services;

namespace KHost.Abstractions.Models.Plugins;

/// <summary>A plugin's entry point, its row, and its context, resolved once services exist.</summary>
/// <param name="discovered">The plugin's row on the Plugins page.</param>
/// <param name="entryPoint">The plugin's own instance, constructed against the host's services.</param>
/// <param name="context">The plugin's manifest, settings and secrets.</param>
public sealed class LoadedPlugin(DiscoveredPlugin discovered, IPlugin entryPoint, IPluginContext context)
{
    /// <summary>The discovery row this plugin was loaded from.</summary>
    public DiscoveredPlugin Discovered { get; } = discovered;

    /// <summary>The plugin's own instance.</summary>
    public IPlugin EntryPoint { get; } = entryPoint;

    /// <summary>The plugin's own manifest, settings and secrets.</summary>
    public IPluginContext Context { get; } = context;
}
