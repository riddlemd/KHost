using System.Text.Json.Serialization;

namespace KHost.UserInterface.Models;

/// <summary>
/// A theme as data rather than as a stylesheet. Built-in themes are authored as SCSS and compiled
/// at build time, so a theme a host creates at runtime cannot be one of those — it is stored here
/// and rendered to CSS on request instead.
/// </summary>
public sealed class ThemeDefinition
{
    /// <summary>Filename-safe slug. Doubles as the stylesheet URL segment, so it must stay unique.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Set for the themes shipped as SCSS; those are read-only and clone rather than edit.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// Carried on the way in and computed on the way out, never stored: the store's disabled list
    /// is the one authority, and a persisted copy here would read as authoritative while losing.
    /// </summary>
    [JsonIgnore]
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Only the values a host actually chooses (<see cref="ThemeVariableCatalog"/>). The rest of the
    /// 73 properties a theme needs are derived when the stylesheet is built, so a stored theme never
    /// carries a stale copy of something computed.
    /// </summary>
    public Dictionary<string, string> Variables { get; set; } = [];

    public ThemeDefinition CloneAs(string id, string name) => new()
    {
        Id = id,
        Name = name,
        IsBuiltIn = false,
        IsEnabled = true,
        Variables = new Dictionary<string, string>(Variables)
    };

    public string this[string key] => Variables.TryGetValue(key, out var value)
        ? value
        : ThemeVariableCatalog.FallbackFor(key);
}
