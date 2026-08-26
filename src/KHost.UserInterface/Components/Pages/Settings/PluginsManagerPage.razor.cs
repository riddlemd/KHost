using System.Net;
using System.Text.Json;
using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Plugins;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using KHost.Plugins.Sdk.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class PluginsManagerPage : IDisposable
{
    [Inject] private IPluginsService? PluginsService { get; set; }
    [Inject] private IPluginCatalogService? Catalog { get; set; }
    [Inject] private IPluginInstallerService? Installer { get; set; }
    [Inject] private IDialogService? Dialogs { get; set; }
    [Inject] private IExternalLinkService? ExternalLinks { get; set; }
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private readonly string _pluginsDirectory = PluginPaths.Plugins;
    private readonly Dictionary<string, List<SettingField>> _settingFields = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _openIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _savedIds = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _enabledIds = new(StringComparer.OrdinalIgnoreCase);

    private Tab _tab = Tab.Installed;
    private PluginStagingState _staging = PluginStagingState.Empty;
    private bool _catalogBusy;

    private IReadOnlyList<DiscoveredPlugin> Plugins => PluginsService?.Plugins ?? [];

    private IEnumerable<string> WaitingOnRestart => Plugins
        .Where(p => GetRowState(p) is RowState.RestartToLoad or RowState.RestartToUnload)
        .Select(p => p.DisplayName);

    protected override async Task OnInitializedAsync()
    {
        if (PluginsService is null) return;

        _subscriptions.Add(Broker.Subscribe<PluginsChanged>(OnStateChanged));
        _subscriptions.Add(Broker.Subscribe<PluginCatalogChanged>(OnStateChanged));
        _subscriptions.Add(Broker.Subscribe<PluginInstallsChanged>(OnInstallsChanged));

        _staging = Installer?.Staged() ?? PluginStagingState.Empty;

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

    private void OnStateChanged(object message) => InvokeAsync(StateHasChanged);

    // Staging is read from disk, not held in memory, so it has to be re-read whenever an install
    // moves — that is the only signal that a payload landed or a pending action was dropped.
    private void OnInstallsChanged(PluginInstallsChanged message) => InvokeAsync(() =>
    {
        _staging = Installer?.Staged() ?? PluginStagingState.Empty;

        StateHasChanged();
    });

    public void Dispose() => _subscriptions.Dispose();


    private IReadOnlyList<PluginCatalogEntry> CatalogEntries => Catalog?.Current?.Catalog.Plugins ?? [];

    private string StagingSummary
    {
        get
        {
            var parts = new List<string>();

            if (_staging.Installs.Count > 0) parts.Add($"{_staging.Installs.Count} to install");
            if (_staging.Removals.Count > 0) parts.Add($"{_staging.Removals.Count} to remove");
            if (_staging.Failures.Count > 0) parts.Add($"{_staging.Failures.Count} failed");

            return string.Join(", ", parts);
        }
    }

    private static int Percent(double fraction) => (int)Math.Round(fraction * 100);

    private async Task SelectTabAsync(Tab tab)
    {
        _tab = tab;

        // Fetched on open rather than at startup: a console runs on whatever wifi the room has,
        // and nothing on the installed list needs the network.
        if (tab == Tab.Available && Catalog is not null && Catalog.Current is null)
            await LoadCatalogAsync(force: false);
    }

    private Task RefreshCatalogAsync() => LoadCatalogAsync(force: true);

    private async Task LoadCatalogAsync(bool force)
    {
        if (Catalog is null || _catalogBusy) return;

        _catalogBusy = true;

        try
        {
            if (force) await Catalog.RefreshAsync();
            else await Catalog.GetAsync();
        }
        finally
        {
            _catalogBusy = false;
        }
    }

    private string? GetInstalledVersion(Guid pluginId)
        => Plugins.FirstOrDefault(p => p.Manifest?.Id == pluginId)?.Manifest?.Version;

    private PluginInstallInfo? GetActiveInstall(Guid pluginId)
        => Installer?.Snapshot().FirstOrDefault(i =>
            i.PluginId == pluginId && i.State is PluginInstallState.Downloading or PluginInstallState.Verifying);

    private PluginInstallInfo? GetLastInstall(Guid pluginId)
        => Installer?.Snapshot().FirstOrDefault(i => i.PluginId == pluginId);

    private AvailableState GetAvailableState(PluginCatalogEntry entry)
    {
        if (GetActiveInstall(entry.Id) is not null) return AvailableState.Installing;
        if (_staging.Failures.ContainsKey(entry.Id)) return AvailableState.StageFailed;
        if (_staging.Installs.Contains(entry.Id)) return AvailableState.Staged;
        if (_staging.Removals.Contains(entry.Id)) return AvailableState.PendingRemoval;

        var installed = GetInstalledVersion(entry.Id);
        var release = entry.LatestCompatible();

        if (release is null)
        {
            if (installed is not null) return AvailableState.Installed;

            return entry.HasReleaseForThisHost() ? AvailableState.Unverified : AvailableState.Incompatible;
        }

        if (installed is null) return AvailableState.Installable;

        return PluginVersion.IsNewer(release.Version, installed) ? AvailableState.UpdateAvailable : AvailableState.Installed;
    }

    private async Task ConfirmInstallAsync(PluginCatalogEntry entry)
    {
        if (Dialogs is null || entry.LatestCompatible() is not { } release) return;

        var name = WebUtility.HtmlEncode(entry.Name);
        var author = WebUtility.HtmlEncode(entry.Author ?? "an unnamed publisher");
        var installed = GetInstalledVersion(entry.Id);
        var verb = installed is null ? "Install" : "Update";

        await Dialogs.ShowConfirmationAsync(
            $"<p>{name} {WebUtility.HtmlEncode(release.Version)} is published by {author}.</p>"
            + "<p>A plugin runs inside KHost with the same access to this machine as KHost itself. "
            + "Install it only if you trust its publisher.</p>",
            onConfirm: () =>
            {
                // Not awaited: the confirmation dialog closes on its callback returning, and a
                // download would hold it open for the length of the transfer.
                _ = InstallAsync(entry, release);

                return Task.CompletedTask;
            },
            title: $"{verb} {entry.Name}",
            confirmText: verb);
    }

    private async Task InstallAsync(PluginCatalogEntry entry, PluginCatalogRelease release)
    {
        if (Installer is null) return;

        var result = await Installer.InstallAsync(entry, release);

        // Enabling is the Plugins service's to record, not the installer's — the host asked for
        // this plugin by installing it, so it should be on when the payload lands.
        if (result.State == PluginInstallState.Staged && PluginsService is not null)
        {
            var id = entry.Id.ToString();

            await PluginsService.SetEnabledAsync(id, true);

            _enabledIds.Add(id);

            await InvokeAsync(StateHasChanged);
        }
    }

    private void CancelInstall(Guid pluginId) => Installer?.Cancel(pluginId);

    private void ClearStaged(Guid pluginId) => Installer?.ClearStaged(pluginId);

    private async Task ConfirmUninstallAsync(DiscoveredPlugin plugin)
    {
        if (Dialogs is null || Installer is null || plugin.Manifest is not { } manifest) return;

        await Dialogs.ShowConfirmationAsync(
            $"<p>Remove {WebUtility.HtmlEncode(plugin.DisplayName)} and its folder on the next start?</p>"
            + "<p>Its saved settings are kept, so reinstalling restores them.</p>",
            onConfirm: async () =>
            {
                Installer.MarkForRemoval(manifest.Id);

                if (PluginsService is not null)
                {
                    await PluginsService.SetEnabledAsync(manifest.Id.ToString(), false);

                    _enabledIds.Remove(manifest.Id.ToString());
                }
            },
            title: $"Remove {plugin.DisplayName}",
            confirmText: "Remove");
    }

    private void OpenRepository(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme is "https" or "http")
        {
            ExternalLinks?.Open(uri.ToString());
        }
    }

    private static string GetAvailableGlyph(PluginCatalogEntry entry) => entry.Capabilities switch
    {
        var c when c.Contains("Media provider") => "music-note-beamed",
        var c when c.Contains("Queue rotation") => "people-fill",
        var c when c.Contains("Break music") => "music-player",
        _ => "puzzle",
    };


    private enum Tab
    {
        Installed,
        Available,
    }

    private enum AvailableState
    {
        Installable,
        UpdateAvailable,
        Installed,
        Installing,
        Staged,
        StageFailed,
        PendingRemoval,
        /// <summary>The catalog lists it, but no release targets this host's plugin API.</summary>
        Incompatible,
        /// <summary>A release targets this host, but is published without an https URL and a
        /// checksum — so the host has no way to know it got what the catalog described.</summary>
        Unverified,
    }

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
