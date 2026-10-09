using System.Net;
using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.Common.Plugins;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

/// <summary>The Available tab: the catalog list, install/update/undo actions and their in-flight
/// progress. Loads itself on first render rather than the page orchestrating it, since it exists
/// only while that tab is selected — the same "fetched on open, not at startup" rule as before.</summary>
public partial class AvailablePluginsTab
{
    [Inject] private IPluginCatalogService Catalog { get; set; } = default!;
    [Inject] private IPluginInstallerService Installer { get; set; } = default!;
    [Inject] private IPluginsService PluginsService { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private IExternalLinkService ExternalLinks { get; set; } = default!;

    [Parameter, EditorRequired] public PluginStagingState Staging { get; set; } = PluginStagingState.Empty;
    [Parameter, EditorRequired] public string PluginsDirectory { get; set; } = "";
    /// <summary>The page's own SetEnabledAsync: _enabledIds is shared with the Installed tab, so
    /// this tab mutates it through the page rather than keeping a second copy.</summary>
    [Parameter, EditorRequired] public Func<string, bool, Task> SetEnabledAsync { get; set; } = default!;

    private bool _catalogBusy;

    private IReadOnlyList<DiscoveredPlugin> Plugins => PluginsService.Plugins;

    private IReadOnlyList<PluginCatalogEntry> CatalogEntries => Catalog.Current?.Catalog.Plugins ?? [];

    protected override async Task OnInitializedAsync()
    {
        // Fetched on open rather than at startup: a console runs on whatever wifi the room has,
        // and nothing on the installed list needs the network.
        if (Catalog.Current is null)
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
        if (Staging.Failures.ContainsKey(entry.Id)) return AvailableState.StageFailed;
        if (Staging.Installs.Contains(entry.Id)) return AvailableState.Staged;
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
        // Read before clearing: the page's PluginInstallsChanged subscription re-reads staging
        // from disk after this, so the pre-clear snapshot has to be taken here.
        var staged = Staging;

        Installer.ClearStaged(pluginId);

        var id = pluginId.ToString();

        if (staged.Installs.Contains(pluginId))
        {
            // Only a first install enabled anything. Undoing an update leaves the installed copy
            // running, so disabling there would switch off a plugin the host never touched.
            if (GetInstalledVersion(pluginId) is null)
                await SetEnabledAsync(id, false);
        }
        else if (IsPendingRemoval(pluginId, staged) && PluginsManagerPage.WasLoadedAtStartup(Plugins, pluginId))
        {
            // Loaded is the only honest signal that it was enabled when this process started; a
            // plugin already switched off before the removal was marked stays off.
            await SetEnabledAsync(id, true);
        }
    }

    /// <summary>The Available tab has a catalog id and no folder, so it answers for any copy.</summary>
    private bool IsPendingRemoval(Guid pluginId, PluginStagingState? staging = null)
    {
        var removals = (staging ?? Staging).Removals;

        return Plugins.Any(p => p.Manifest?.Id == pluginId && removals.Contains(PluginsManagerPage.FolderNameOf(p)));
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
        => entry.DescribeApiRefusal() ?? $"The catalog publishes no build for {PluginRid.Current}.";

    /// <summary>A catalog entry has no manifest to declare an icon with, so this stays the generic
    /// glyph until install, kept as a method so the row reads the same as the installed one.</summary>
    private static string GetAvailableGlyph(PluginCatalogEntry entry) => PluginsManagerPage.DefaultGlyph;

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
}
