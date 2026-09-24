using System.Text.Json;

namespace KHost.Abstractions.Models.Plugins;

/// <summary>Persisted plugin state, read at startup before DI exists; keep the shapes in sync.</summary>
public class PluginsState
{
    /// <summary>Ids of plugins the host should load; a discovered plugin not listed here stays
    /// disabled.</summary>
    public List<string> EnabledPluginIds { get; set; } = [];

    /// <summary>Per-plugin stored setting values, keyed by plugin id then setting key.</summary>
    public Dictionary<string, Dictionary<string, JsonElement>> Settings { get; set; } = [];

    /// <summary>The cache key this state is stored under.</summary>
    public const string CacheKey = "Plugins";
}
