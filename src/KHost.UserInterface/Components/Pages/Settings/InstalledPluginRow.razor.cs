using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.Common.Plugins;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

/// <summary>One row of the Installed tab: its header (glyph, identity, state badge, enable
/// toggle) and, while open, its settings form and foot actions. Uninstall/keep-it stay callbacks
/// into the page rather than moving here, because both need to check every discovered plugin for a
/// second copy under another folder before touching the shared enabled flag — data this row, which
/// knows only its own Plugin, cannot see.</summary>
public partial class InstalledPluginRow
{
    [Inject] private IPluginButtonService PluginButtons { get; set; } = default!;
    [Inject] private IExternalLinkService ExternalLinks { get; set; } = default!;

    [Parameter, EditorRequired] public DiscoveredPlugin Plugin { get; set; } = default!;
    [Parameter] public PluginSettingsDraft? Draft { get; set; }
    [Parameter, EditorRequired] public bool Enabled { get; set; }
    [Parameter, EditorRequired] public bool Open { get; set; }
    [Parameter, EditorRequired] public bool PendingRemoval { get; set; }
    [Parameter, EditorRequired] public PluginsManagerPage.RowState State { get; set; }
    [Parameter, EditorRequired] public IReadOnlySet<string> RunningButtons { get; set; } = new HashSet<string>();

    [Parameter] public EventCallback OnToggleOpen { get; set; }
    /// <summary>Forwarded from PluginSettingsForm: an edit there leaves this row's own Revert/Save/
    /// Saved buttons stale until the page redraws with the fresh dirty/saved state.</summary>
    [Parameter] public EventCallback OnChanged { get; set; }
    [Parameter, EditorRequired] public Func<string, bool, Task> SetEnabledAsync { get; set; } = default!;
    [Parameter, EditorRequired] public Func<string, Task> SaveSettingsAsync { get; set; } = default!;
    [Parameter, EditorRequired] public Action<string> Revert { get; set; } = default!;
    [Parameter, EditorRequired] public Func<string, string, Task> RunButtonAsync { get; set; } = default!;
    [Parameter, EditorRequired] public Func<DiscoveredPlugin, Task> ConfirmUninstallAsync { get; set; } = default!;
    [Parameter, EditorRequired] public Func<DiscoveredPlugin, Task> ClearRemovalAsync { get; set; } = default!;

    private bool CanEnable => Plugin.Status is not (PluginStatus.Errored or PluginStatus.Incompatible) || Enabled;

    // Enabled is a plain parameter recomputed by the page from _enabledIds; unlike Draft (a shared
    // mutable reference this row's own re-render already sees fresh), SetEnabledAsync's write is
    // invisible here until the page redraws and passes a fresh value back down.
    private async Task OnEnabledChangedAsync(bool enabled)
    {
        await SetEnabledAsync(Plugin.Id, enabled);
        await OnChanged.InvokeAsync();
    }

    private void OpenFolder(string directory)
    {
        if (Directory.Exists(directory))
            ExternalLinks.Open(directory);
    }

    /// <summary>Manifest-only, never guessed: guessing would let the same plugin wear a different
    /// glyph installed than in the catalog; worn by anything that has not said otherwise.</summary>
    private static string GetGlyph(DiscoveredPlugin plugin)
        => plugin.Manifest?.Icon is { Length: > 0 } icon
           && !string.Equals(icon, PluginIcon.ImageSpecifier, StringComparison.OrdinalIgnoreCase)
            ? icon
            : PluginsManagerPage.DefaultGlyph;

    private static string GetStateLabel(PluginsManagerPage.RowState state) => state switch
    {
        PluginsManagerPage.RowState.Running => "Running",
        PluginsManagerPage.RowState.RestartToLoad => "Restart to load",
        PluginsManagerPage.RowState.RestartToUnload => "Restart to unload",
        PluginsManagerPage.RowState.Failed => "Failed",
        PluginsManagerPage.RowState.Incompatible => "Incompatible",
        _ => "Off",
    };

    private static string GetStateBadgeClass(PluginsManagerPage.RowState state) => state switch
    {
        PluginsManagerPage.RowState.Running => "kh-badge--success",
        PluginsManagerPage.RowState.RestartToLoad or PluginsManagerPage.RowState.RestartToUnload => "kh-badge--warning",
        PluginsManagerPage.RowState.Failed => "kh-badge--danger",
        PluginsManagerPage.RowState.Incompatible => "kh-badge--info",
        _ => "kh-badge--ext",
    };

    /// <summary>An unknown style falls back to primary, not a class that resolves to nothing.</summary>
    private static string ButtonStyleClass(string? style) => style switch
    {
        "secondary" => "kh-button--secondary",
        "danger" => "kh-button--danger",
        _ => "kh-button--primary",
    };
}
