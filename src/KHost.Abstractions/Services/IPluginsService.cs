using KHost.Abstractions.Models.Plugins;
using System.Text.Json;

namespace KHost.Abstractions.Services;

/// <summary>Which plugins are enabled and what their settings are, as saved for the next start.</summary>
/// <remarks>Host-only: the Plugins page. A plugin reads its own settings through
/// <see cref="IPluginContext"/>, not this. Every change is saved at once and applies only after a
/// restart. A host singleton, callable from any thread; saves do not overlap. Each save announces
/// <see cref="KHost.Abstractions.Messaging.Messages.PluginsChanged"/>.</remarks>
public interface IPluginsService
{
    /// <summary>Plugins discovered at startup; statuses reflect that load, not later edits.</summary>
    IReadOnlyList<DiscoveredPlugin> Plugins { get; }

    /// <summary>True once anything has been saved this process, so what runs no longer matches
    /// what the next start will load.</summary>
    bool RestartRequired { get; }

    /// <summary>The ids of the plugins set to load on the next start.</summary>
    /// <remarks>Ids match without regard to case.</remarks>
    Task<IReadOnlySet<string>> ReadEnabledIdsAsync();

    /// <summary>Sets whether a plugin loads on the next start.</summary>
    Task SetEnabledAsync(string pluginId, bool enabled);

    /// <summary>A plugin's saved settings, by key.</summary>
    /// <returns>Empty when none are saved; the manifest's defaults are not included.</returns>
    Task<Dictionary<string, JsonElement>> ReadSettingsAsync(string pluginId);

    /// <summary>Replaces a plugin's saved settings with <paramref name="values"/>.</summary>
    Task SaveSettingsAsync(string pluginId, Dictionary<string, JsonElement> values);
}
