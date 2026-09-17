using KHost.Abstractions.Services;

namespace KHost.Abstractions.Models.Plugins;

/// <summary>A plugin's entry point, its row, and its context, resolved once services exist.</summary>
public sealed class LoadedPlugin(DiscoveredPlugin discovered, IPlugin entryPoint, IPluginContext context)
{
    public DiscoveredPlugin Discovered { get; } = discovered;

    public IPlugin EntryPoint { get; } = entryPoint;

    public IPluginContext Context { get; } = context;
}
