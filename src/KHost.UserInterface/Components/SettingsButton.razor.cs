using KHost.UserInterface.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KHost.UserInterface.Components;

public partial class SettingsButton : IDisposable
{
    private const string HomeRoute = "/";
    private const string VenueSection = "venue";
    private const string ThemeSection = "theme";
    private const string ManageGroup = "Manage";
    private const string ApplicationGroup = "Application";

    [Inject] private NavigationManager? NavigationManager { get; set; }
    [Inject] private IPermissionService? Permissions { get; set; }
    [Inject] private IAppSettingsService? AppSettings { get; set; }
    [Inject] private IVenuesService? VenuesService { get; set; }
    [Inject] private IThemeService? ThemeService { get; set; }
    [Inject] private IBreakMusicService? BreakMusic { get; set; }
    [Inject] private IDialogService? DialogService { get; set; }
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private sealed class SettingsPage
    {
        public string Title { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Route { get; set; } = "";
        public string Group { get; set; } = ManageGroup;

        /// <summary>Null means any signed-in user; the page itself enforces the same rule.</summary>
        public KHostPermission? Requires { get; set; }

        public bool AdminOnly { get; set; }

        /// <summary>Set for a page that only means anything under some other setting.</summary>
        public Func<SettingsButton, bool>? Applies { get; set; }

        /// <summary>Set instead of Route for an item that opens a dialog rather than navigating.</summary>
        public Func<SettingsButton, Task>? Opens { get; set; }
    }

    private static readonly List<SettingsPage> _allPages =
    [
        // Manage is listed alphabetically by title; Application below is not, so keep new entries
        // in place rather than appending.
        new SettingsPage { Title = "Ads Manager", Icon = "megaphone", Route = "/settings/ads-manager", Requires = KHostPermission.ManageMedia },
        new SettingsPage { Title = "Break Music Manager", Icon = "music-note-beamed", Route = "/settings/break-music-manager", Requires = KHostPermission.ManageMedia, Applies = menu => menu.VenuePlaysLocalBreakMusic },
        new SettingsPage { Title = "Downloads Manager", Icon = "cloud-download", Route = "/settings/downloads-manager", Requires = KHostPermission.ManageMedia },
        new SettingsPage { Title = "Media Manager", Icon = "music-note-list", Route = "/settings/media-manager", Requires = KHostPermission.ManageMedia },
        new SettingsPage { Title = "Plugins Manager", Icon = "plug-fill", Route = "/settings/plugins-manager", AdminOnly = true },
        new SettingsPage { Title = "Theme Manager", Icon = "palette-fill", Route = "/settings/theme-manager", AdminOnly = true },
        new SettingsPage { Title = "Tips Manager", Icon = "coin", Route = "/settings/tips-manager", Applies = menu => menu.VenueTakesTips },
        new SettingsPage { Title = "User Groups Manager", Icon = "people-fill", Route = "/settings/user-groups-manager", Requires = KHostPermission.EditGroup },
        new SettingsPage { Title = "Users Manager", Icon = "person-fill", Route = "/settings/users-manager", Requires = KHostPermission.EditUser },
        new SettingsPage { Title = "Venues Manager", Icon = "geo-alt-fill", Route = "/settings/venues-manager", Requires = KHostPermission.EditVenue },
        new SettingsPage { Title = "App Settings", Icon = "gear-fill", Route = "/settings/app-settings", Group = ApplicationGroup, AdminOnly = true },
        new SettingsPage { Title = "Keyboard Shortcuts", Icon = "keyboard", Group = ApplicationGroup, Opens = menu => menu.ShowShortcutsAsync() },
        new SettingsPage { Title = "About", Icon = "info-circle", Route = "/settings/about", Group = ApplicationGroup }
    ];

    private bool _canLock;
    private string? _openSection;

    private DropdownMenu? _menu;
    private IJSObjectReference? _module;
    private ElementReference _venueRowRef;
    private ElementReference _themeRowRef;
    private ElementReference _flyoutRef;
    private IReadOnlyList<Venue> _venues = [];
    private Venue? _selectedVenue;

    private List<IGrouping<string, SettingsPage>> _groups = [];

    protected override async Task OnInitializedAsync()
    {
        // Locking a console that signs everyone in automatically would be a button to nowhere.
        _canLock = AppSettings?.Current.RequireLogin != false;

        if (VenuesService is not null)
        {
            _subscriptions.Add(Broker.Subscribe<VenuesChanged>(_ => QueueRebuild()));

            // Not VenuesChanged: that one fires for any venue's edit, and which pages apply is a
            // question about the venue the console is running.
            _subscriptions.Add(Broker.Subscribe<SelectedVenueChanged>(_ => QueueRebuild()));
        }

        _subscriptions.Add(Broker.Subscribe<ThemeChanged>(_ => QueueRebuild()));
        _subscriptions.Add(Broker.Subscribe<BreakMusicChanged>(_ => QueueRebuild()));
        _subscriptions.Add(Broker.Subscribe<ThemesChanged>(_ => QueueRebuild()));

        await RebuildAsync();
    }

    // Handlers run in subscription order and a slow one holds up the rest, so the rebuild is
    // started rather than awaited here.
    private void QueueRebuild() => _ = RebuildAsync();

    /// <summary>
    /// The venue decides both which venues the switcher lists and which pages apply, so one rebuild
    /// serves every message that can move either. The venue is read before the list is filtered:
    /// judging a venue-dependent page against a venue that has not arrived yet hides it.
    /// </summary>
    private async Task RebuildAsync()
    {
        await RefreshVenuesAsync();

        _groups = [.. (await VisiblePagesAsync()).GroupBy(page => page.Group)];

        await InvokeAsync(StateHasChanged);
    }

    private async Task RefreshVenuesAsync()
    {
        if (VenuesService is null) return;

        var result = await VenuesService.ReadAllAsync(pageSize: 1000);
        _selectedVenue = await VenuesService.ReadSelectedVenueAsync();

        // Disabled venues are managed, not sung at: they stay in the venues manager but not in this
        // switcher. The selected one always shows, so disabling the venue in use never makes the
        // menu lie about where tonight's queue is running.
        _venues = [.. result.Items.Where(v => v.Enabled || v.Id == _selectedVenue?.Id)];
    }

    /// <summary>
    /// The break music playlists only feed one mode. A venue playing Spotify has nothing to manage
    /// there, so the page goes rather than sitting in the menu describing someone else's music.
    /// Asked of the running provider rather than of RendersThroughHost, which says who plays the
    /// audio — a provider may render through the host and still bring its own catalogue.
    /// </summary>
    /// <summary>
    /// Tips are a venue's choice and the manager is a list of them, so a venue that does not take
    /// them has nothing to show there. No venue at all counts as not taking them — there is nothing
    /// for a tip to belong to yet.
    /// </summary>
    private bool VenueTakesTips => _selectedVenue?.Settings.TippingEnabled ?? false;

    private bool VenuePlaysLocalBreakMusic
        => BreakMusic?.LibraryProvider is { } library
           && BreakMusic.ActiveProvider is { } active
           && string.Equals(active.SourceName, library.SourceName, StringComparison.OrdinalIgnoreCase);

    private bool IsOpen(string section) => _openSection == section;

    // A section must not outlive the menu it was opened in, or the next open shows a flyout nobody
    // asked for — and one that was never placed, because opening the menu does not render this.
    private void OnMenuOpenChanged(bool open)
    {
        if (!open) _openSection = null;

        StateHasChanged();
    }

    // Placed after every render while a section is open: the row it hangs off moves whenever the
    // menu re-renders, and a flyout left where the row used to be is worse than none.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_openSection is null) return;

        _module ??= await JS.InvokeAsync<IJSObjectReference>("import", "/js/dropdown-menu.js");

        await _module.InvokeVoidAsync("positionFlyout",
            IsOpen(VenueSection) ? _venueRowRef : _themeRowRef, _flyoutRef);
    }

    // One at a time: both open at once pushes the managers off the bottom of the menu.
    private void ToggleSection(string section) => _openSection = IsOpen(section) ? null : section;

    private async Task SelectVenueAsync(Guid venueId)
    {
        if (VenuesService is not null)
            await VenuesService.SelectVenueAsync(venueId);

        CloseMenu();
    }

    private async Task SelectThemeAsync(string theme)
    {
        if (ThemeService is not null)
            await ThemeService.SetThemeAsync(theme);

        CloseMenu();
    }

    private async Task EditVenueAsync()
    {
        if (VenuesService is null || DialogService is null || _selectedVenue is null) return;

        CloseMenu();

        await DialogService.RequestEditAsync(_selectedVenue, async updated =>
        {
            if (updated is not null)
                await VenuesService.UpdateAsync(updated);
        });
    }

    // A custom theme carries a name of its own; only a built-in is named by its filename.
    private string ThemeName(string? theme)
        => string.IsNullOrEmpty(theme) ? "" : ThemeService?.DisplayNameFor(theme) ?? theme;

    // The menu keeps itself open so a section can expand in place, so anything that finishes a
    // choice has to close it by hand.
    private void CloseMenu()
    {
        _openSection = null;
        _menu?.Close();
    }

    private async Task<List<SettingsPage>> VisiblePagesAsync()
    {
        if (Permissions is null) return _allPages;

        var visible = new List<SettingsPage>();
        var isAdmin = await Permissions.IsAdminAsync();

        foreach (var page in _allPages)
        {
            var allowed = page switch
            {
                { AdminOnly: true } => isAdmin,
                { Requires: { } permission } => await Permissions.HasAsync(permission),
                _ => true,
            };

            if (allowed && (page.Applies?.Invoke(this) ?? true)) visible.Add(page);
        }

        return visible;
    }

    private async Task SelectAsync(SettingsPage page)
    {
        CloseMenu();

        if (page.Opens is { } open)
            await open(this);
        else
            NavigationManager?.NavigateTo(page.Route);
    }

    private Task ShowShortcutsAsync() => DialogService?.ShowShortcutsAsync() ?? Task.CompletedTask;

    private void NavigateTo(string route)
    {
        CloseMenu();
        NavigationManager?.NavigateTo(route);
    }

    // Read as the menu opens rather than tracked: the items are a fragment, so this runs each time
    // the menu is rendered and there is no navigation to subscribe to.
    private string CurrentClass(string route)
        => !string.IsNullOrEmpty(route) && IsSameRoute(new Uri(NavigationManager?.Uri ?? HomeRoute).AbsolutePath, route)
            ? "kh-dropdown__item--current"
            : "";

    public static bool IsSameRoute(string path, string route)
        => string.Equals(path.TrimEnd('/'), route.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _subscriptions.Dispose();
    }
}
