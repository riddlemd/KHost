using System.Text.Json;

namespace KHost.Abstractions.Models.Plugins;

/// <summary>
/// Persisted plugin state (cache/plugins.json). Read directly at startup before DI exists;
/// written through ICacheService with key "Plugins" — keep the shapes in sync.
/// </summary>
public class PluginsState
{
    public List<string> EnabledPluginIds { get; set; } = [];

    /// <summary>Per-plugin stored setting values, keyed by plugin id then setting key.</summary>
    public Dictionary<string, Dictionary<string, JsonElement>> Settings { get; set; } = [];

    public const string CacheKey = "Plugins";
}
