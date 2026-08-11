using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;

namespace KHost.Domain.Services.Plugins;

public class PluginRegistry(IReadOnlyList<DiscoveredPlugin> plugins) : IPluginRegistry
{
    public IReadOnlyList<DiscoveredPlugin> Plugins { get; } = plugins;
}
