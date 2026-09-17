using System.Text.Json.Serialization;

namespace KHost.UserInterface.Models;

/// <summary>A theme as data rather than a stylesheet; built-ins are authored as SCSS at build time.</summary>
/// <remarks>A runtime-created theme is stored here and rendered on request instead.</remarks>
public sealed class ThemeDefinition
{
    /// <summary>Filename-safe slug. Doubles as the stylesheet URL segment, so it must stay unique.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Set for the themes shipped as SCSS; those are read-only and clone rather than edit.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Never stored: the store's disabled list is the one authority on IsEnabled.</summary>
    [JsonIgnore]
    public bool IsEnabled { get; set; } = true;

    /// <summary>Only the values a host actually chooses (<see cref="ThemeVariableCatalog"/>).</summary>
    /// <remarks>The rest are derived when the stylesheet is built, so nothing stored goes stale.</remarks>
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
