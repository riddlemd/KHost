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
    // Installer only for ClearRemovalAsync; the catalog/install flow lives in AvailablePluginsTab.
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

    private async Task SetEnabledAsync(string pluginId, bool enabled)
    {
        await PluginsService.SetEnabledAsync(pluginId, enabled);

        if (enabled) _enabledIds.Add(pluginId);
        else _enabledIds.Remove(pluginId);
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
    internal const string DefaultGlyph = "puzzle";

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

    private void SelectTab(Tab tab) => _tab = tab;

    /// <summary>The folder a row stands for. Two rows may share a manifest id (a plugin dropped
    /// in by hand under a second name), and only this tells them apart.</summary>
    internal static string FolderNameOf(DiscoveredPlugin plugin) => Path.GetFileName(
        plugin.Directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    internal static bool WasLoadedAtStartup(IReadOnlyList<DiscoveredPlugin> plugins, Guid pluginId)
        => plugins.Any(p => p.Manifest?.Id == pluginId && p.Status == PluginStatus.Loaded);

    private bool IsPendingRemoval(DiscoveredPlugin plugin) => _staging.Removals.Contains(FolderNameOf(plugin));

    /// <summary>Undoes a removal: re-enabling is keyed by id, valid only if a copy loaded.</summary>
    private async Task ClearRemovalAsync(DiscoveredPlugin plugin)
    {
        if (plugin.Manifest is not { } manifest) return;

        var restoreEnabled = WasLoadedAtStartup(Plugins, manifest.Id);

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

    private enum Tab
    {
        Installed,
        Available,
    }

    public enum RowState
    {
        Running,
        RestartToLoad,
        RestartToUnload,
        Off,
        Failed,
        Incompatible,
    }

}
