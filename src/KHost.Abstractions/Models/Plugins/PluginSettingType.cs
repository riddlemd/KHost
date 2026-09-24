namespace KHost.Abstractions.Models.Plugins;

/// <summary>Which control a <see cref="PluginSettingDefinition"/> is shown as.</summary>
public enum PluginSettingType
{
    /// <summary>A single-line text field.</summary>
    String,

    /// <summary>A whole-number field.</summary>
    Int,

    /// <summary>A checkbox.</summary>
    Bool,
}
