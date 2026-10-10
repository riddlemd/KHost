using System.Text.Json;
using System.Text.Json.Serialization;

namespace KHost.Abstractions.Models.Plugins;

/// <summary>One setting a plugin asks the host to collect on the Plugins page, and to hand back
/// through its own <see cref="Services.IPluginContext"/>.</summary>
public class PluginSettingDefinition
{
    /// <summary>Identifies this setting; a plugin reads its value back by this key. Unique within
    /// the plugin's own manifest.</summary>
    public required string Key { get; set; }

    /// <summary>Which control the host shows and how the stored value is typed.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<PluginSettingType>))]
    public required PluginSettingType Type { get; set; }

    /// <summary>Shown beside the control on the Plugins page.</summary>
    public required string Label { get; set; }

    /// <summary>A line of explanation shown under the control on the Plugins page, or null for
    /// none.</summary>
    /// <remarks>Says what the setting does or what leaving it blank means, so the label can stay a
    /// short name.</remarks>
    public string? Description { get; set; }

    /// <summary>Marks the setting as one the plugin works without; the Plugins page says so beside
    /// the label.</summary>
    /// <remarks>Not drawn for <see cref="PluginSettingType.Bool"/>, which always holds a value.
    /// The host enforces nothing either way: a plugin still reads a missing value as missing.</remarks>
    public bool Optional { get; set; }

    /// <summary>Heading this setting sits under on the Plugins page, or null to sit above the
    /// first one.</summary>
    /// <remarks>Grouped by first appearance, so the order settings are declared in is the order
    /// the sections come out; a manifest that names none renders exactly as it always did.</remarks>
    public string? Section { get; set; }

    /// <summary>Masks the input on screen. Says nothing about how the value is stored.</summary>
    public bool Secret { get; set; }

    /// <summary>Value shown before the host has one on record; null leaves the control empty.</summary>
    public JsonElement? Default { get; set; }
}
