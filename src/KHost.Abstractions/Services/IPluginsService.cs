using KHost.Abstractions.Models.Plugins;
using System.Text.Json;

namespace KHost.Abstractions.Services;

public interface IPluginsService
{
    /// <summary>Plugins discovered at startup; statuses reflect that load, not later edits.</summary>
    IReadOnlyList<DiscoveredPlugin> Plugins { get; }

    /// <summary>True once the persisted state differs from what this process loaded with.</summary>
    bool RestartRequired { get; }

    Task<IReadOnlySet<string>> ReadEnabledIdsAsync();
    Task SetEnabledAsync(string pluginId, bool enabled);
    Task<Dictionary<string, JsonElement>> ReadSettingsAsync(string pluginId);
    Task SaveSettingsAsync(string pluginId, Dictionary<string, JsonElement> values);
}
