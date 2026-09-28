using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Plugins;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Net;
using KHost.Common.Plugins;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class PluginsManagerPage : IDisposable
{
    [Inject] private IPluginsService PluginsService { get; set; } = default!;
    [Inject] private IPluginButtonService PluginButtons { get; set; } = default!;
    [Inject] private IPluginCatalogService Catalog { get; set; } = default!;
    [Inject] private IPluginInstallerService Installer { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private IExternalLinkService ExternalLinks { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;
    [Inject] private ILogger<PluginsManagerPage> Logger { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private readonly string _pluginsDirectory = PluginPaths.Plugins;
    private readonly Dictionary<string, PluginSettingsDraft> _drafts = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Which rows are expanded, by folder rather than by plugin id: two rows may carry one
    /// id, and opening either would otherwise open both.</summary>
    private readonly HashSet<string> _openFolders = new(StringComparer.Ordinal);
    /// <summary>"pluginId:key" of buttons whose action is still running, so a click cannot re-enter
    /// a login prompt that is already open.</summary>
    private readonly HashSet<string> _runningButtons = new(StringComparer.Ordinal);
    private HashSet<string> _enabledIds = new(StringComparer.OrdinalIgnoreCase);

    private Tab _tab = Tab.Installed;
    private PluginStagingState _staging = PluginStagingState.Empty;
    private bool _catalogBusy;

    private IReadOnlyList<DiscoveredPlugin> Plugins => PluginsService.Plugins;

    private IEnumerable<string> WaitingOnRestart => Plugins
        .Where(p => GetRowState(p) is RowState.RestartToLoad or RowState.RestartToUnload)
        .Select(p => p.DisplayName);

    protected override async Task OnInitializedAsync()
    {
        _subscriptions.Add(Broker.Subscribe<PluginsChanged>(OnStateChanged));
        _subscriptions.Add(Broker.Subscribe<PluginCatalogChanged>(OnStateChanged));
        _subscriptions.Add(Broker.Subscribe<PluginInstallsChanged>(OnInstallsChanged));

        _staging = Installer.Staged();

        _enabledIds = (await PluginsService.ReadEnabledIdsAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in Plugins.Where(p => p.Manifest?.Settings.Count > 0))
        {
            var stored = await PluginsService.ReadSettingsAsync(plugin.Id);

            // Spotify puts the Spicetify bridge next to the port it uses; PluginSettingsDraft.Build
            // keeps declared order for exactly that reason.
            _drafts[plugin.Id] = PluginSettingsDraft.Build(plugin.Manifest!.Settings, stored);
        }
    }

    private bool IsOpen(DiscoveredPlugin plugin) => _openFolders.Contains(FolderNameOf(plugin));

    private void ToggleOpen(DiscoveredPlugin plugin)
    {
        var folder = FolderNameOf(plugin);

        if (!_openFolders.Add(folder))
            _openFolders.Remove(folder);
    }

    private bool IsEnabled(string pluginId) => _enabledIds.Contains(pluginId);

    private bool IsDirty(string pluginId)
        => _drafts.TryGetValue(pluginId, out var draft) && draft.IsDirty;

    private bool WasSaved(string pluginId) => _drafts.TryGetValue(pluginId, out var draft) && draft.Saved;

    private static bool CanEnable(DiscoveredPlugin plugin, bool enabled)
        => plugin.Status is not (PluginStatus.Errored or PluginStatus.Incompatible) || enabled;

    private async Task SetEnabledAsync(string pluginId, bool enabled)
    {
        await PluginsService.SetEnabledAsync(pluginId, enabled);

        if (enabled) _enabledIds.Add(pluginId);
        else _enabledIds.Remove(pluginId);
    }

    /// <summary>Any edit invalidates the "Saved" marker, so it can never describe stale state.</summary>
    private void MarkEdited(string pluginId)
    {
        if (_drafts.TryGetValue(pluginId, out var draft))
            draft.MarkEdited();
    }

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
        if (_drafts.TryGetValue(pluginId, out var draft))
            draft.Revert();
    }

    private async Task SaveSettingsAsync(string pluginId)
    {
        if (!_drafts.TryGetValue(pluginId, out var draft)) return;

        await PluginsService.SaveSettingsAsync(pluginId, draft.ToValues());

        draft.CommitAll();
    }

    private void OpenFolder(string directory)
    {
        if (Directory.Exists(directory))
            ExternalLinks.Open(directory);
    }

    private static string GetInputType(PluginSettingDefinition definition)
        => definition.Type == PluginSettingType.Int ? "number" : "text";

    /// <summary>An unknown style falls back to primary, not a class that resolves to nothing.</summary>
    private static string ButtonStyleClass(string? style) => style switch
    {
        "secondary" => "kh-button--secondary",
        "danger" => "kh-button--danger",
        _ => "kh-button--primary",
    };

    /// <summary>Runs a plugin's button and re-reads its state. Nothing else redraws the row when
    /// login reports "Sign out"; blocked from re-entering while already running.</summary>
    private async Task RunButtonAsync(string pluginId, string key)
    {
        var token = $"{pluginId}:{key}";
        if (!_runningButtons.Add(token))
            return;

        try
        {
            await PluginButtons.InvokeAsync(pluginId, key);
        }
        catch (Exception ex)
        {
            // A plugin's button throwing is the plugin's problem to report; the page stays up.
            Logger.LogWarning(ex, "Plugin button {Key} on {PluginId} threw", key, pluginId);
        }
        finally
        {
            _runningButtons.Remove(token);
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>Manifest-only, never guessed: guessing would let the same plugin wear a different
    /// glyph installed than in the catalog; worn by anything that has not said otherwise.</summary>
    private const string DefaultGlyph = "puzzle";

    private static string GetGlyph(DiscoveredPlugin plugin)
        => plugin.Manifest?.Icon is { Length: > 0 } icon
           && !string.Equals(icon, PluginIcon.ImageSpecifier, StringComparison.OrdinalIgnoreCase)
            ? icon
            : DefaultGlyph;

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
    // moves. That is the only signal that a payload landed or a pending action was dropped.
    private void OnInstallsChanged(PluginInstallsChanged message) => InvokeAsync(() =>
    {
        _staging = Installer.Staged();

        StateHasChanged();
    });

    public void Dispose() => _subscriptions.Dispose();


    private IReadOnlyList<PluginCatalogEntry> CatalogEntries => Catalog.Current?.Catalog.Plugins ?? [];

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

    private async Task SelectTabAsync(Tab tab)
    {
        _tab = tab;

        // Fetched on open rather than at startup: a console runs on whatever wifi the room has,
        // and nothing on the installed list needs the network.
        if (tab == Tab.Available && Catalog.Current is null)
            await LoadCatalogAsync(force: false);
    }

    private Task RefreshCatalogAsync() => LoadCatalogAsync(force: true);

    private async Task LoadCatalogAsync(bool force)
    {
        if (_catalogBusy) return;

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
        => Installer.Snapshot().FirstOrDefault(i =>
            i.PluginId == pluginId && i.State is PluginInstallState.Downloading or PluginInstallState.Verifying);

    private PluginInstallInfo? GetLastInstall(Guid pluginId)
        => Installer.Snapshot().FirstOrDefault(i => i.PluginId == pluginId);

    private AvailableState GetAvailableState(PluginCatalogEntry entry)
    {
        if (GetActiveInstall(entry.Id) is not null) return AvailableState.Installing;
        if (_staging.Failures.ContainsKey(entry.Id)) return AvailableState.StageFailed;
        if (_staging.Installs.Contains(entry.Id)) return AvailableState.Staged;
        if (IsPendingRemoval(entry.Id)) return AvailableState.PendingRemoval;

        var installed = GetInstalledVersion(entry.Id);
        var release = entry.LatestCompatibleRelease();

        if (release is null)
        {
            if (installed is not null) return AvailableState.Installed;

            // Another platform is not compatible either, so it shares the badge. Unverifiable
            // earns its own: that plugin would run here, and only its publisher can fix it.
            if (!entry.HasReleaseForThisHost() || !entry.HasReleaseForThisPlatform())
                return AvailableState.Incompatible;

            return AvailableState.Unverified;
        }

        if (installed is null) return AvailableState.Installable;

        return PluginVersion.IsNewer(release.Version, installed) ? AvailableState.UpdateAvailable : AvailableState.Installed;
    }

    private async Task ConfirmInstallAsync(PluginCatalogEntry entry)
    {
        if (entry.LatestCompatibleRelease() is not { } release) return;

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
        var result = await Installer.InstallAsync(entry, release);

        // Enabling is the Plugins service's to record, not the installer's. The host asked for
        // this plugin by installing it, so it should be on when the payload lands.
        if (result.State == PluginInstallState.Staged)
        {
            var id = entry.Id.ToString();

            await SetEnabledAsync(id, true);

            await InvokeAsync(StateHasChanged);
        }
    }

    private void CancelInstall(Guid pluginId) => Installer.Cancel(pluginId);

    /// <summary>Installing enables a plugin and marking one for removal disables it, so undoing
    /// either has to put that flag back.</summary>
    private async Task ClearStagedAsync(Guid pluginId)
    {
        // Read before clearing: the announce that follows re-reads staging from disk.
        var staged = _staging;

        Installer.ClearStaged(pluginId);

        var id = pluginId.ToString();

        if (staged.Installs.Contains(pluginId))
        {
            // Only a first install enabled anything. Undoing an update leaves the installed copy
            // running, so disabling there would switch off a plugin the host never touched.
            if (GetInstalledVersion(pluginId) is null)
                await SetEnabledAsync(id, false);
        }
        else if (IsPendingRemoval(pluginId, staged) && WasLoadedAtStartup(pluginId))
        {
            // Loaded is the only honest signal that it was enabled when this process started; a
            // plugin already switched off before the removal was marked stays off.
            await SetEnabledAsync(id, true);
        }
    }

    private bool WasLoadedAtStartup(Guid pluginId)
        => Plugins.Any(p => p.Manifest?.Id == pluginId && p.Status == PluginStatus.Loaded);

    /// <summary>The folder a row stands for. Two rows may share a manifest id (a plugin dropped
    /// in by hand under a second name), and only this tells them apart.</summary>
    private static string FolderNameOf(DiscoveredPlugin plugin) => Path.GetFileName(
        plugin.Directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private bool IsPendingRemoval(DiscoveredPlugin plugin) => _staging.Removals.Contains(FolderNameOf(plugin));

    /// <summary>The Available tab has a catalog id and no folder, so it answers for any copy.</summary>
    private bool IsPendingRemoval(Guid pluginId, PluginStagingState? staging = null)
    {
        var removals = (staging ?? _staging).Removals;

        return Plugins.Any(p => p.Manifest?.Id == pluginId && removals.Contains(FolderNameOf(p)));
    }

    /// <summary>Undoes a removal: re-enabling is keyed by id, valid only if a copy loaded.</summary>
    private async Task ClearRemovalAsync(DiscoveredPlugin plugin)
    {
        if (plugin.Manifest is not { } manifest) return;

        var restoreEnabled = WasLoadedAtStartup(manifest.Id);

        Installer.ClearRemoval(FolderNameOf(plugin));

        if (!restoreEnabled) return;

        await SetEnabledAsync(manifest.Id.ToString(), true);
    }

    private async Task ConfirmUninstallAsync(DiscoveredPlugin plugin)
    {
        if (plugin.Manifest is not { } manifest) return;

        await Dialogs.ShowConfirmationAsync(
            $"<p>Remove {WebUtility.HtmlEncode(plugin.DisplayName)} and its folder on the next start?</p>"
            + "<p>Its saved settings are kept, so reinstalling restores them.</p>",
            onConfirm: async () =>
            {
                Installer.MarkForRemoval(FolderNameOf(plugin));

                // The enabled flag is the id's, not the folder's: switching it off while another
                // copy of the same plugin stays installed would disable the copy that is running.
                if (Plugins.Count(p => p.Manifest?.Id == manifest.Id) > 1) return;

                await SetEnabledAsync(manifest.Id.ToString(), false);
            },
            title: $"Remove {plugin.DisplayName}",
            confirmText: "Remove");
    }

    private void OpenRepository(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme is "https" or "http")
        {
            ExternalLinks.Open(uri.ToString());
        }
    }

    /// <summary>Why nothing is installable, for the badge's tooltip: the two causes ask different
    /// things of a host.</summary>
    private static string IncompatibleReason(PluginCatalogEntry entry)
        => entry.HasReleaseForThisHost()
            ? $"The catalog publishes no build for {PluginRid.Current}."
            : "No release targets this host's plugin API.";

    /// <summary>A catalog entry has no manifest to declare an icon with, so this stays the generic
    /// glyph until install, kept as a method so the row reads the same as the installed one.</summary>
    private static string GetAvailableGlyph(PluginCatalogEntry entry) => DefaultGlyph;


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
        /// <summary>Nothing the catalog lists will run here: wrong plugin API, or no build for
        /// this platform. <see cref="IncompatibleReason"/> says which.</summary>
        Incompatible,
        /// <summary>A release targets this host, but is published without an https URL and a
        /// checksum, so the host has no way to know it got what the catalog described.</summary>
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

}
