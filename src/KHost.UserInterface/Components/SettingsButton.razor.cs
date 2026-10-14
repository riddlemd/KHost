using KHost.UserInterface.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
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

    // [Inject] throws at property-injection time when the type is not registered, whatever the
    // nullable annotation says, so a plain `is not null` guard below it can never be false: it was
    // dead code, not an optional-service check.
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private IVenuesService VenuesService { get; set; } = default!;
    [Inject] private IThemeService ThemeService { get; set; } = default!;

    // The one real exception: IEnumerable resolves to an empty sequence rather than throwing, so
    // "no display plugin installed" is a legitimate empty collection, not a missing service.
    [Inject] private IEnumerable<IDisplayProvider> DisplayProviders { get; set; } = [];
    [Inject] private IBreakMusicService BreakMusic { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;
    [Inject] private ILogger<SettingsButton> Logger { get; set; } = default!;
    [Inject] private IConfiguration Configuration { get; set; } = default!;
    [Inject] private IHostApplicationLifetime Lifetime { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private sealed class SettingsPage
    {
        public string Title { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Route { get; set; } = "";
        public string Group { get; set; } = ManageGroup;

        /// <summary>Set for a page that only means anything under some other setting.</summary>
        public Func<SettingsButton, bool>? Applies { get; set; }
    }

    private static readonly List<SettingsPage> _allPages =
    [
        // Manage is listed alphabetically by title; Application below is not, so keep new entries
        // in place rather than appending.
        new SettingsPage { Title = "Ads Manager", Icon = "megaphone", Route = "/settings/ads-manager" },
        new SettingsPage { Title = "Break Music Manager", Icon = "music-note-beamed", Route = "/settings/break-music-manager", Applies = menu => menu.VenuePlaysLocalBreakMusic },
        new SettingsPage { Title = "Downloads Manager", Icon = "cloud-download", Route = "/settings/downloads-manager" },
        new SettingsPage { Title = "Media Manager", Icon = "music-note-list", Route = "/settings/media-manager" },
        new SettingsPage { Title = "Plugins Manager", Icon = "plug-fill", Route = "/settings/plugins-manager" },
        new SettingsPage { Title = "Theme Manager", Icon = "palette-fill", Route = "/settings/theme-manager" },
        new SettingsPage { Title = "Tips Manager", Icon = "coin", Route = "/settings/tips-manager", Applies = menu => menu.VenueTakesTips },
        new SettingsPage { Title = "User Groups Manager", Icon = "people-fill", Route = "/settings/user-groups-manager" },
        new SettingsPage { Title = "Users Manager", Icon = "person-fill", Route = "/settings/users-manager" },
        new SettingsPage { Title = "Venues Manager", Icon = "geo-alt-fill", Route = "/settings/venues-manager" },
        new SettingsPage { Title = "Visualisations Manager", Icon = "stars", Route = "/settings/visualisations-manager" },
        new SettingsPage { Title = "App Settings", Icon = "gear-fill", Route = "/settings/app-settings", Group = ApplicationGroup },
        new SettingsPage { Title = "About", Icon = "info-circle", Route = "/settings/about", Group = ApplicationGroup }
    ];

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
    private IReadOnlyList<IDisplayProvider> _displays = [];

    protected override async Task OnInitializedAsync()
    {
        // Fixed for the component's lifetime: the set of registered providers does not change at
        // runtime, only their connections do, so copying it once here spares every render the
        // allocation a fresh `[.. DisplayProviders]` on each access would cost.
        _displays = [.. DisplayProviders];

        _subscriptions.Add(Broker.Subscribe<VenuesChanged>(_ => QueueRebuild()));

        // Not VenuesChanged: that one fires for any venue's edit, and which pages apply is a
        // question about the venue the console is running.
        _subscriptions.Add(Broker.Subscribe<SelectedVenueChanged>(_ => QueueRebuild()));

        _subscriptions.Add(Broker.Subscribe<ThemeChanged>(_ => QueueRedraw()));

        // A sweep finding a receiver, or a screen arriving, changes what this row says without
        // anyone touching the menu.
        _subscriptions.Add(Broker.Subscribe<DisplaysChanged>(message => QueueRedraw()));
        _subscriptions.Add(Broker.Subscribe<BreakMusicChanged>(_ => QueueRebuild()));
        _subscriptions.Add(Broker.Subscribe<ThemesChanged>(_ => QueueRedraw()));

        await RebuildAsync();
    }

    // Handlers run in subscription order and a slow one holds up the rest, so the rebuild is
    // started rather than awaited here. Dispatched through InvokeAsync so the venue
    // reads inside RebuildAsync land on the renderer thread, not the broker's.
    private void QueueRebuild() => _ = InvokeAsync(RebuildAsync);

    // Announced off the render thread. Themes and displays change no page's visibility, so they
    // need only a redraw, not a re-read of venues.
    private void QueueRedraw() => _ = InvokeAsync(StateHasChanged);

    /// <summary>Reads the venue before filtering, or a venue-dependent page is judged too soon.</summary>
    private async Task RebuildAsync()
    {
        await RefreshVenuesAsync();

        _groups = [.. VisiblePages().GroupBy(page => page.Group)];

        await InvokeAsync(StateHasChanged);
    }

    private async Task RefreshVenuesAsync()
    {
        var result = await VenuesService.ReadAllAsync(pageSize: 1000);
        _selectedVenue = await VenuesService.ReadSelectedVenueAsync();

        // Disabled venues stay in the manager but drop from this switcher, except the selected
        // one, so disabling it mid-use can't make the menu lie about where tonight's queue runs.
        _venues = [.. result.Items.Where(v => v.Enabled || v.Id == _selectedVenue?.Id)];
    }

    /// <summary>No venue at all counts as not taking tips: nothing yet for a tip to belong to.</summary>
    private bool VenueTakesTips => _selectedVenue?.Settings.TippingEnabled ?? false;

    /// <summary>Checked against the running provider, not the chosen one, which may not be loaded.</summary>
    private bool VenuePlaysLocalBreakMusic
        => BreakMusic.LibraryProvider is { } library
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

    // --- display ---

    internal IReadOnlyList<IDisplayProvider> Displays => _displays;

    /// <summary>The one display carrying the song, if any.</summary>
    internal IDisplayProvider? LiveDisplay
        => Displays.FirstOrDefault(display => display.ConnectedDeviceId is { Length: > 0 });

    /// <summary>What the row reads on the right: the device, not the transport. A host reads the
    /// room, not the wiring.</summary>
    internal string DisplayValue => DisplayValueFor(LiveDisplay);

    /// <summary>Takes the live display rather than reading <see cref="LiveDisplay"/> itself, so the
    /// markup can compute it once per render and hand it to both this and the flyout's own check
    /// instead of walking <see cref="Displays"/> twice.</summary>
    private static string DisplayValueFor(IDisplayProvider? live)
        => live is null ? "None" : DeviceOf(live)?.Name ?? live.Name;

    private static DisplayDevice? DeviceOf(IDisplayProvider display)
        => display.Devices.FirstOrDefault(device => device.Id == display.ConnectedDeviceId)
            ?? display.Devices.FirstOrDefault(device => device.IsConnected);

    /// <summary>Headless only, for now: the native window still has a close button of its own.</summary>
    private bool CanExit => !Configuration.GetValue<bool>(Program.NativeShellKey);

    private async Task ExitAsync()
    {
        CloseMenu();

        await DialogService.ShowConfirmationAsync(
            "KHost will stop and close the screens it opened.",
            onConfirm: StopHostAsync,
            title: "Exit KHost",
            confirmText: "Exit");
    }

    private async Task StopHostAsync()
    {
        try
        {
            await using var module = await JS.InvokeAsync<IJSObjectReference>("import", "/js/host-exit.js");
            await module.InvokeVoidAsync("showStopped");
        }
        catch (JSException ex)
        {
            // A page that cannot say it stopped must not keep the host running.
            Logger.LogWarning(ex, "Could not show the stopped page before exiting");
        }

        // Off the circuit: StopApplication runs every shutdown hook synchronously on the caller,
        // and a hook blocking on work that needs this circuit would never finish.
        _ = Task.Run(Lifetime.StopApplication);
    }

    // The menu keeps itself open so a section can expand in place, so anything that finishes a
    // choice has to close it by hand.
    private void CloseMenu()
    {
        _openSection = null;
        _menu?.Close();
    }

    private List<SettingsPage> VisiblePages()
        => [.. _allPages.Where(page => page.Applies?.Invoke(this) ?? true)];

    private void NavigateTo(string route)
    {
        CloseMenu();
        NavigationManager.NavigateTo(route);
    }

    // Read as the menu opens rather than tracked: the items are a fragment, so this runs each time
    // the menu is rendered and there is no navigation to subscribe to.
    private string CurrentClass(string route)
        => !string.IsNullOrEmpty(route) && IsSameRoute(new Uri(NavigationManager.Uri).AbsolutePath, route)
            ? "kh-dropdown__item--current"
            : "";

    public static bool IsSameRoute(string path, string route)
        => string.Equals(path.TrimEnd('/'), route.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _subscriptions.Dispose();
    }
}
