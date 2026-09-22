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
    private const string DisplaySection = "display";
    private const string ManageGroup = "Manage";
    private const string ApplicationGroup = "Application";

    [Inject] private NavigationManager? NavigationManager { get; set; }
    [Inject] private IPermissionService? Permissions { get; set; }
    [Inject] private IAppSettingsService? AppSettings { get; set; }
    [Inject] private IVenuesService? VenuesService { get; set; }
    [Inject] private IThemeService? ThemeService { get; set; }

    // [Inject] resolves by type and ignores the nullable annotation, so this takes the enumerable:
    // with no display plugin installed it is simply empty.
    [Inject] private IEnumerable<IDisplayProvider> DisplayProviders { get; set; } = [];
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
    private ElementReference _displayRowRef;
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

        // A sweep finding a receiver, or a screen arriving, changes what this row says without
        // anyone touching the menu.
        _subscriptions.Add(Broker.Subscribe<DisplaysChanged>(message => QueueRedraw()));
        _subscriptions.Add(Broker.Subscribe<BreakMusicChanged>(_ => QueueRebuild()));
        _subscriptions.Add(Broker.Subscribe<ThemesChanged>(_ => QueueRebuild()));

        await RebuildAsync();
    }

    // Handlers run in subscription order and a slow one holds up the rest, so the rebuild is
    // started rather than awaited here.
    private void QueueRebuild() => _ = RebuildAsync();

    // Announced off the render thread, and nothing here needs re-reading — only redrawing.
    private void QueueRedraw() => _ = InvokeAsync(StateHasChanged);

    /// <summary>Reads the venue before filtering, or a venue-dependent page is judged too soon.</summary>
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

        // Disabled venues stay in the manager but drop from this switcher, except the selected
        // one, so disabling it mid-use can't make the menu lie about where tonight's queue runs.
        _venues = [.. result.Items.Where(v => v.Enabled || v.Id == _selectedVenue?.Id)];
    }

    /// <summary>No venue at all counts as not taking tips: nothing yet for a tip to belong to.</summary>
    private bool VenueTakesTips => _selectedVenue?.Settings.TippingEnabled ?? false;

    /// <summary>Checked against the running provider, not RendersThroughHost, which it may bypass.</summary>
    private bool VenuePlaysLocalBreakMusic
        => BreakMusic?.LibraryProvider is { } library
           && BreakMusic.ActiveProvider is { } active
           && string.Equals(active.SourceName, library.SourceName, StringComparison.OrdinalIgnoreCase);

    private bool IsOpen(string section) => _openSection == section;

    // A section must not outlive the menu it was opened in, or the next open shows a flyout nobody
    // asked for and never placed, since opening the menu does not render this.
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

        await _module.InvokeVoidAsync("positionFlyout", RowFor(_openSection), _flyoutRef);
    }

    private ElementReference RowFor(string? section) => section switch
    {
        VenueSection => _venueRowRef,
        DisplaySection => _displayRowRef,
        _ => _themeRowRef,
    };

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

    // --- display ---

    internal IReadOnlyList<IDisplayProvider> Displays => [.. DisplayProviders];

    /// <summary>The one display carrying the song, if any.</summary>
    internal IDisplayProvider? LiveDisplay
        => Displays.FirstOrDefault(display => display.ConnectedDeviceId is { Length: > 0 });

    /// <summary>What the row reads on the right: the device, not the transport. A host reads the
    /// room, not the wiring.</summary>
    internal string DisplayValue
    {
        get
        {
            if (LiveDisplay is not { } live) return "None";

            return DeviceOf(live)?.Name ?? live.Name;
        }
    }

    private static DisplayDevice? DeviceOf(IDisplayProvider display)
        => display.Devices.FirstOrDefault(device => device.Id == display.ConnectedDeviceId)
            ?? display.Devices.FirstOrDefault(device => device.IsConnected);

    /// <summary>Every device every transport knows about, live one included — unlike a split
    /// button's primary half, a checked row in a list is the ordinary way to show the current
    /// choice, and it sits beside the others so switching back is one press.</summary>
    internal IEnumerable<(IDisplayProvider Provider, DisplayDevice Device)> AvailableDisplays()
    {
        foreach (var provider in Displays)
        {
            IReadOnlyList<DisplayDevice> devices;

            // A provider mid-sweep can throw here, and one bad plugin must not empty the list.
            try { devices = provider.Devices; }
            catch { continue; }

            foreach (var device in devices)
                yield return (provider, device);
        }
    }

    internal static bool IsLive(IDisplayProvider provider, DisplayDevice device)
        => provider.ConnectedDeviceId == device.Id;

    /// <summary>Names the transport under the device, so two rooms called "TV" are still telling.</summary>
    internal static string DescribeDisplay(IDisplayProvider provider, DisplayDevice device)
    {
        var model = string.IsNullOrWhiteSpace(device.Model) ? provider.Name : device.Model;

        return string.IsNullOrWhiteSpace(device.Address) ? model : $"{model} · {device.Address}";
    }

    /// <summary>True while any transport is actually sweeping — asked of the providers, never
    /// tracked here: discovery outlives the call that started it, so a flag of our own would say
    /// "searching" for five seconds and then lie for the rest of the night.</summary>
    internal bool IsSearching => Displays.Any(display => display.SearchesForDevices && display.IsDiscovering);

    /// <summary>Nothing to offer when every transport opens its own device rather than finding one.</summary>
    internal bool CanSearch => Displays.Any(display => display.SearchesForDevices);

    /// <summary>Switches the song to a device, taking it off whatever had it.</summary>
    internal async Task SelectDisplayAsync(IDisplayProvider provider, DisplayDevice device)
    {
        // Already there: a press on the checked row should not tear the song down and rebuild it.
        if (IsLive(provider, device))
        {
            CloseMenu();
            return;
        }

        // Off first, and every provider, or a press that fails below leaves the song on two.
        foreach (var other in Displays)
        {
            if (other == provider) continue;
            if (other.ConnectedDeviceId is not { Length: > 0 }) continue;

            await SafelyAsync(() => other.DisconnectAsync());
        }

        await SafelyAsync(() => provider.ConnectAsync(device.Id));

        CloseMenu();
    }

    internal async Task TurnOffDisplayAsync()
    {
        foreach (var display in Displays)
        {
            if (display.ConnectedDeviceId is not { Length: > 0 }) continue;

            await SafelyAsync(() => display.DisconnectAsync());
        }

        CloseMenu();
    }

    /// <summary>One control for both halves, because the answer is one piece of state: a sweep is
    /// either running or it is not. Stopping matters — a console runs all night on whatever wifi
    /// the room has, and browsing is off until someone asks for it.</summary>
    internal async Task ToggleSearchAsync()
    {
        var searching = IsSearching;

        foreach (var provider in Displays)
        {
            // The screens are opened, not found: asking them to discover launches a screen, which
            // is not what pressing "search" asked for.
            if (!provider.SearchesForDevices) continue;

            await SafelyAsync(() => searching
                ? provider.StopDiscoveryAsync()
                : provider.StartDiscoveryAsync());
        }

        // Deliberately left open: a sweep fills the list underneath, and closing the menu would
        // hide the very thing that was asked for.
        StateHasChanged();
    }

    /// <summary>A transport that throws must not take the menu down with it.</summary>
    private static async Task SafelyAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception) { }
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
