using System.Text.Json;
using KHost.Abstractions.Models.Plugins;

namespace KHost.UserInterface.Models;

/// <summary>One plugin's settings form: its fields, whether any is unsaved, and the "Saved" flash
/// that a fresh edit clears. Bundled here so PluginsManagerPage and PluginSettingsForm read one
/// object instead of two parallel dictionaries keyed by plugin id.</summary>
internal sealed class PluginSettingsDraft
{
    public required IReadOnlyList<SettingField> Fields { get; init; }

    /// <summary>Cleared by MarkEdited, so a stale flash never survives the next keystroke.</summary>
    public bool Saved { get; private set; }

    public bool IsDirty => Fields.Any(f => f.IsDirty);

    /// <summary>Declared order, deliberately: an author groups settings by meaning, and nothing
    /// here knows better.</summary>
    public IReadOnlyList<SettingSection> Sections => SectionsOf(Fields);

    public void MarkEdited() => Saved = false;

    public void Revert()
    {
        foreach (var field in Fields)
            field.Reset();

        MarkEdited();
    }

    /// <summary>What SaveSettingsAsync sends: a plugin's whole value set, so an omitted key is a
    /// deletion of whatever it used to hold.</summary>
    public Dictionary<string, JsonElement> ToValues()
    {
        var values = new Dictionary<string, JsonElement>();

        foreach (var field in Fields)
        {
            if (field.ToJson() is { } value)
                values[field.Definition.Key] = value;
        }

        return values;
    }

    public void CommitAll()
    {
        foreach (var field in Fields)
            field.Commit();

        Saved = true;
    }

    public static PluginSettingsDraft Build(IReadOnlyList<PluginSettingDefinition> definitions, Dictionary<string, JsonElement> stored)
    {
        // Declared order, deliberately: an author groups settings by meaning, and nothing here
        // knows better.
        var fields = definitions.Select(definition => BuildField(definition, stored)).ToList();

        var draft = new PluginSettingsDraft { Fields = fields };
        draft.CommitAll();

        return draft;
    }

    private static SettingField BuildField(PluginSettingDefinition definition, Dictionary<string, JsonElement> stored)
    {
        stored.TryGetValue(definition.Key, out var storedValue);

        var hasStored = storedValue.ValueKind != JsonValueKind.Undefined;
        var field = new SettingField { Definition = definition };

        if (definition.Secret)
        {
            // The value itself never reaches the markup, only whether one is held, so a saved key
            // stops looking like a never-set one.
            field.StoredSecret = hasStored && storedValue.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(storedValue.GetString())
                ? storedValue
                : null;
        }
        else
        {
            var element = hasStored ? storedValue : definition.Default;

            field.Text = element?.ValueKind is JsonValueKind.String ? element.Value.GetString() : element?.ToString();
            field.Flag = element?.ValueKind is JsonValueKind.True;
        }

        return field;
    }

    /// <summary>Groups by first appearance, so the order settings are declared in decides the
    /// order the headings come out, and a manifest that names none renders as one unheaded run
    /// exactly as it did before sections existed.</summary>
    public static IReadOnlyList<SettingSection> SectionsOf(IReadOnlyList<SettingField> fields)
    {
        var sections = new List<SettingSection>();
        var byName = new Dictionary<string, List<SettingField>>(StringComparer.OrdinalIgnoreCase);
        List<SettingField>? unheaded = null;

        foreach (var field in fields)
        {
            // Blank is the same as absent: a manifest with "section": "" means the author has not
            // grouped it, and an empty heading would draw a rule with nothing above it.
            var name = string.IsNullOrWhiteSpace(field.Definition.Section) ? null : field.Definition.Section.Trim();

            if (name is null)
            {
                // Still the first run wherever it appears: a setting left ungrouped after a
                // heading belongs with the ungrouped ones, not orphaned under somebody else's.
                unheaded ??= [];
                unheaded.Add(field);

                continue;
            }

            if (!byName.TryGetValue(name, out var group))
            {
                byName[name] = group = [];
                sections.Add(new SettingSection(name, group));
            }

            group.Add(field);
        }

        return unheaded is null ? sections : [new SettingSection(null, unheaded), .. sections];
    }
}

/// <summary>One heading and the settings under it. A null name is the run before any heading,
/// which is what a manifest naming no sections produces for all of them.</summary>
internal sealed record SettingSection(string? Name, IReadOnlyList<SettingField> Fields);

internal sealed class SettingField
{
    public required PluginSettingDefinition Definition { get; init; }

    public string? Text { get; set; }

    public bool Flag { get; set; }

    /// <summary>The persisted secret, held so saving an unrelated field cannot drop it.
    /// SaveSettingsAsync replaces a plugin's whole value set, and an omitted key is a deletion.</summary>
    public JsonElement? StoredSecret { get; set; }

    /// <summary>True while the host is typing a new secret over one already stored.</summary>
    public bool Replacing { get; set; }

    public string? OriginalText { get; private set; }

    public bool OriginalFlag { get; private set; }

    public JsonElement? OriginalSecret { get; private set; }

    public bool HasSecret => StoredSecret is not null;

    /// <summary>Last four characters, so a host can tell which key is stored without it being
    /// shown. Short values reveal too much of themselves to hint at.</summary>
    public string? SecretHint => StoredSecret?.GetString() is { Length: > 8 } value ? value[^4..] : null;

    public bool IsDirty => Definition switch
    {
        { Type: PluginSettingType.Bool } => Flag != OriginalFlag,
        { Secret: true } => Replacing ? !string.IsNullOrEmpty(Text) : HasSecret != (OriginalSecret is not null),
        _ => Text != OriginalText,
    };

    public JsonElement? ToJson()
    {
        if (Definition.Secret)
        {
            if (Replacing && !string.IsNullOrEmpty(Text))
                return JsonSerializer.SerializeToElement(Text);

            return StoredSecret;
        }

        return Definition.Type switch
        {
            PluginSettingType.Bool => JsonSerializer.SerializeToElement(Flag),
            // Unparseable input is omitted so the plugin falls back to its manifest default.
            PluginSettingType.Int => int.TryParse(Text, out var number) ? JsonSerializer.SerializeToElement(number) : null,
            _ => string.IsNullOrEmpty(Text) ? null : JsonSerializer.SerializeToElement(Text),
        };
    }

    public void Commit()
    {
        if (Definition.Secret && Replacing && !string.IsNullOrEmpty(Text))
            StoredSecret = JsonSerializer.SerializeToElement(Text);

        if (Definition.Secret)
        {
            Replacing = false;
            Text = null;
        }

        OriginalText = Text;
        OriginalFlag = Flag;
        OriginalSecret = StoredSecret;
    }

    public void Reset()
    {
        Replacing = false;
        Text = OriginalText;
        Flag = OriginalFlag;
        StoredSecret = OriginalSecret;
    }
}
