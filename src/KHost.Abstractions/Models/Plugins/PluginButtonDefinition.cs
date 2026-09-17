namespace KHost.Abstractions.Models.Plugins;

/// <summary>A Plugins-page row button; runs via <see cref="Services.IPluginButtonHandler"/>.</summary>
public class PluginButtonDefinition
{
    public required string Key { get; set; }

    /// <summary>Wording when unoverridden; see <see cref="Services.PluginButtonState.Label"/>.</summary>
    public required string Label { get; set; }

    /// <summary>A <c>kh-button</c> modifier; an unknown value falls back to the default.</summary>
    public string? Style { get; set; }
}
