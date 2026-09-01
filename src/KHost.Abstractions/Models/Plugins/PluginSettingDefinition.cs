using System.Text.Json;
using System.Text.Json.Serialization;

namespace KHost.Abstractions.Models.Plugins;

public class PluginSettingDefinition
{
    public required string Key { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<PluginSettingType>))]
    public required PluginSettingType Type { get; set; }

    public required string Label { get; set; }
    public bool Secret { get; set; }
    public JsonElement? Default { get; set; }
}
