using System.Text.Json;
using System.Text.Json.Serialization;

namespace KHost.Abstractions.Models.Plugins;

public class PluginSettingDefinition
{
    public required string Key { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<PluginSettingType>))]
    public required PluginSettingType Type { get; set; }

    public required string Label { get; set; }

    /// <summary>Heading this setting sits under on the Plugins page, or null to sit above the
    /// first one.</summary>
    /// <remarks>Grouped by first appearance, so the order settings are declared in is the order
    /// the sections come out; a manifest that names none renders exactly as it always did.</remarks>
    public string? Section { get; set; }

    public bool Secret { get; set; }
    public JsonElement? Default { get; set; }
}
