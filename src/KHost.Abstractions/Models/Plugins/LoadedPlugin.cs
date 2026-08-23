using KHost.Plugins.Sdk.Services;

namespace KHost.Abstractions.Models.Plugins;

/// <summary>
/// A plugin's entry point paired with the row it reports against and the context it is handed.
/// Registered during discovery, resolved once the container exists — the two phases the loader
/// cannot collapse, since it runs before there are services to hand out.
/// </summary>
public sealed class LoadedPlugin(DiscoveredPlugin discovered, IPlugin entryPoint, IPluginContext context)
{
    public DiscoveredPlugin Discovered { get; } = discovered;

    public IPlugin EntryPoint { get; } = entryPoint;

    public IPluginContext Context { get; } = context;
}
