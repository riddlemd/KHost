using System.Text.Json;
using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using KHost.Plugins.Sdk.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class PluginsManagerPage : IDisposable
{
    [Inject] private IPluginsService? PluginsService { get; set; }
    [Inject] private IExternalLinkService? ExternalLinks { get; set; }
    [Inject] private IMessageBroker? Broker { get; set; }

    private readonly SubscriptionSet _subscriptions = new();

    private readonly string _pluginsDirectory = Path.Combine(AppContext.BaseDirectory, "plugins");
    private readonly Dictionary<string, List<SettingField>> _settingFields = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _openIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _savedIds = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _enabledIds = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<DiscoveredPlugin> Plugins => PluginsService?.Plugins ?? [];

    private IEnumerable<string> WaitingOnRestart => Plugins
        .Where(p => GetRowState(p) is RowState.RestartToLoad or RowState.RestartToUnload)
        .Select(p => p.DisplayName);

    protected override async Task OnInitializedAsync()
    {
        if (PluginsService is null) return;

        if (Broker is not null)
            _subscriptions.Add(Broker.Subscribe<PluginsChanged>(OnStateChanged));

        _enabledIds = (await PluginsService.ReadEnabledIdsAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in Plugins.Where(p => p.Manifest?.Settings.Count > 0))
        {
            var stored = await PluginsService.ReadSettingsAsync(plugin.Id);

            _settingFields[plugin.Id] = plugin.Manifest!.Settings.Select(definition => Build(definition, stored)).ToList();
        }
    }

    private static SettingField Build(PluginSettingDefinition definition, Dictionary<string, JsonElement> stored)
    {
        stored.TryGetValue(definition.Key, out var storedValue);

        var hasStored = storedValue.ValueKind != JsonValueKind.Undefined;
        var field = new SettingField { Definition = definition };

        if (definition.Secret)
        {
            // The value itself never reaches the markup — only whether one is held, so a saved key
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

        field.Commit();

        return field;
    }

    private bool IsOpen(string pluginId) => _openIds.Contains(pluginId);

    private void ToggleOpen(string pluginId)
    {
        if (!_openIds.Add(pluginId))
            _openIds.Remove(pluginId);
    }

    private bool IsEnabled(string pluginId) => _enabledIds.Contains(pluginId);

    private bool IsDirty(string pluginId)
        => _settingFields.TryGetValue(pluginId, out var fields) && fields.Any(f => f.IsDirty);

    private bool WasSaved(string pluginId) => _savedIds.Contains(pluginId);

    private static bool CanEnable(DiscoveredPlugin plugin, bool enabled)
        => plugin.Status is not (PluginStatus.Errored or PluginStatus.Incompatible) || enabled;

    private async Task ToggleAsync(string pluginId, bool enabled)
    {
        if (PluginsService is null) return;

        await PluginsService.SetEnabledAsync(pluginId, enabled);

        if (enabled) _enabledIds.Add(pluginId);
        else _enabledIds.Remove(pluginId);
    }

    /// <summary>Any edit invalidates the "Saved" marker, so it can never describe stale state.</summary>
    private void MarkEdited(string pluginId) => _savedIds.Remove(pluginId);

    private void ReplaceSecret(string pluginId, SettingField field)
    {
        field.Replacing = true;
        field.Text = null;
        MarkEdited(pluginId);
    }

    private void CancelReplaceSecret(string pluginId, SettingField field)
    {
        field.Replacing = false;
        field.Text = null;
        field.StoredSecret = field.OriginalSecret;
        MarkEdited(pluginId);
    }

    private void ClearSecret(string pluginId, SettingField field)
    {
        field.Replacing = false;
        field.Text = null;
        field.StoredSecret = null;
        MarkEdited(pluginId);
    }

    private void Revert(string pluginId)
    {
        if (!_settingFields.TryGetValue(pluginId, out var fields)) return;

        foreach (var field in fields)
            field.Reset();

        MarkEdited(pluginId);
    }

    private async Task SaveSettingsAsync(string pluginId)
    {
        if (PluginsService is null || !_settingFields.TryGetValue(pluginId, out var fields)) return;

        var values = new Dictionary<string, JsonElement>();

        foreach (var field in fields)
        {
            if (field.ToJson() is { } value)
                values[field.Definition.Key] = value;
        }

        await PluginsService.SaveSettingsAsync(pluginId, values);

        foreach (var field in fields)
            field.Commit();

        _savedIds.Add(pluginId);
    }

    private void OpenFolder(string directory)
    {
        if (Directory.Exists(directory))
            ExternalLinks?.Open(directory);
    }

    private static string GetInputType(PluginSettingDefinition definition)
        => definition.Type == PluginSettingType.Int ? "number" : "text";

    /// <summary>What the plugin provides drives the glyph; a plugin the host never loaded has no
    /// verified capability, so it falls back to the generic one.</summary>
    private static string GetGlyph(DiscoveredPlugin plugin) => plugin.Capabilities switch
    {
        var c when c.Contains("Media provider") => "music-note-beamed",
        var c when c.Contains("Queue rotation") => "people-fill",
        _ => "puzzle",
    };

    private RowState GetRowState(DiscoveredPlugin plugin)
    {
        if (plugin.Status == PluginStatus.Errored) return RowState.Failed;
        if (plugin.Status == PluginStatus.Incompatible) return RowState.Incompatible;

        // Status records the load this process started with; the enabled set records what the host
        // has asked for since. The two disagreeing is exactly what "restart to apply" means.
        return (Loaded: plugin.Status == PluginStatus.Loaded, Enabled: IsEnabled(plugin.Id)) switch
        {
            (true, true) => RowState.Running,
            (true, false) => RowState.RestartToUnload,
            (false, true) => RowState.RestartToLoad,
            (false, false) => RowState.Off,
        };
    }

    private static string GetStateLabel(RowState state) => state switch
    {
        RowState.Running => "Running",
        RowState.RestartToLoad => "Restart to load",
        RowState.RestartToUnload => "Restart to unload",
        RowState.Failed => "Failed",
        RowState.Incompatible => "Incompatible",
        _ => "Off",
    };

    private static string GetStateBadgeClass(RowState state) => state switch
    {
        RowState.Running => "kh-badge--success",
        RowState.RestartToLoad or RowState.RestartToUnload => "kh-badge--warning",
        RowState.Failed => "kh-badge--danger",
        RowState.Incompatible => "kh-badge--info",
        _ => "kh-badge--ext",
    };

    private void OnStateChanged(PluginsChanged message) => InvokeAsync(StateHasChanged);

    public void Dispose() => _subscriptions.Dispose();

    private enum RowState
    {
        Running,
        RestartToLoad,
        RestartToUnload,
        Off,
        Failed,
        Incompatible,
    }

    private sealed class SettingField
    {
        public required PluginSettingDefinition Definition { get; init; }

        public string? Text { get; set; }

        public bool Flag { get; set; }

        /// <summary>The persisted secret, held so saving an unrelated field cannot drop it —
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
}
