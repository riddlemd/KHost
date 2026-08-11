using KHost.Abstractions.Models.Plugins;

namespace KHost.Abstractions.Services;

/// <summary>Every plugin found at startup, whatever its status. Fixed until restart.</summary>
public interface IPluginRegistry
{
    IReadOnlyList<DiscoveredPlugin> Plugins { get; }
}
