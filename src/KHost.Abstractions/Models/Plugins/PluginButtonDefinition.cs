namespace KHost.Abstractions.Models.Plugins;

/// <summary>A Plugins-page row button; runs via <see cref="Services.IPluginButtonHandler"/>.</summary>
public class PluginButtonDefinition
{
    /// <summary>Identifies this button to <see cref="Services.IPluginButtonHandler.InvokeButtonAsync"/>
    /// and <see cref="Services.IPluginButtonHandler.DescribeButton"/>; chosen by the plugin and
    /// unique within its own manifest.</summary>
    public required string Key { get; set; }

    /// <summary>Wording when unoverridden; see <see cref="Services.PluginButtonState.Label"/>.</summary>
    public required string Label { get; set; }

    /// <summary>A <c>kh-button</c> modifier; an unknown value falls back to the default.</summary>
    public string? Style { get; set; }

    /// <summary>Bootstrap Icons name drawn before the label, without the <c>bi-</c> prefix, or
    /// null for a label on its own.</summary>
    /// <remarks>Named rather than supplied, for the same reason the plugin's own <c>icon</c> is:
    /// the host ships one icon set and a plugin that could hand over its own artwork would be
    /// handing over something to draw in the middle of the host's chrome. An unknown name draws
    /// nothing rather than a broken glyph.</remarks>
    public string? Icon { get; set; }
}
