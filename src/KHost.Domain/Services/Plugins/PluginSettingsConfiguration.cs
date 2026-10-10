using System.Text.Json;
using KHost.Abstractions.Models.Plugins;
using Microsoft.Extensions.Configuration;

namespace KHost.Domain.Services.Plugins;

/// <summary>Every plugin's settings as configuration, under <c>PluginSettings:{pluginId}:{key}</c>:
/// its manifest's defaults, with what the host saved on top.</summary>
/// <remarks>Its own root, not the app's <c>Plugins:</c> section, which already holds host keys such as
/// the media folder. A save reloads it, which is what moves a plugin's <c>IOptionsMonitor</c>.</remarks>
public sealed class PluginSettingsConfiguration : ConfigurationProvider, IConfigurationSource
{
    public const string Root = "PluginSettings";

    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<string, PluginSettingDefinition>> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, JsonElement>> _saved = new(StringComparer.OrdinalIgnoreCase);

    public PluginSettingsConfiguration(IReadOnlyDictionary<string, Dictionary<string, JsonElement>>? saved = null)
    {
        foreach (var (pluginId, values) in saved ?? new Dictionary<string, Dictionary<string, JsonElement>>())
            _saved[pluginId] = new(values, StringComparer.OrdinalIgnoreCase);

        Rebuild();
    }

    /// <summary>The section a plugin's settings bind from.</summary>
    public static string SectionFor(string pluginId) => $"{Root}:{pluginId}";

    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    /// <summary>A plugin's manifest settings: the defaults to fall back to, and the type each saved value must have.</summary>
    public void SetDefinitions(string pluginId, IEnumerable<PluginSettingDefinition> definitions)
    {
        lock (_gate)
        {
            _definitions[pluginId] = definitions.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);
            Rebuild();
        }
    }

    /// <summary>Takes a save and reloads, so every listener of this configuration reads the new values.</summary>
    public void SetSaved(string pluginId, IReadOnlyDictionary<string, JsonElement> values)
    {
        lock (_gate)
        {
            _saved[pluginId] = new(values, StringComparer.OrdinalIgnoreCase);
            Rebuild();
        }

        OnReload();
    }

    private void Rebuild()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (pluginId, definitions) in _definitions)
            foreach (var definition in definitions.Values.Where(d => d.Default is not null))
                data[$"{SectionFor(pluginId)}:{definition.Key}"] = TextOf(definition.Default!.Value);

        foreach (var (pluginId, values) in _saved)
        {
            var definitions = _definitions.GetValueOrDefault(pluginId);

            foreach (var (key, value) in values)
            {
                // A hand-edited value of the wrong type would make the binder throw on every read of the
                // plugin's options, so it is dropped and the manifest's default stands.
                if (definitions?.GetValueOrDefault(key) is { } definition && !Fits(value, definition.Type))
                    continue;

                data[$"{SectionFor(pluginId)}:{key}"] = TextOf(value);
            }
        }

        Data = data;
    }

    private static bool Fits(JsonElement value, PluginSettingType type) => type switch
    {
        PluginSettingType.Int => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
        PluginSettingType.Bool => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        _ => value.ValueKind is JsonValueKind.String or JsonValueKind.Null,
    };

    // Configuration is text: a JSON string is its contents, a number or bool its literal, which the
    // binder converts back to the property's type.
    private static string? TextOf(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => value.GetRawText(),
    };
}
