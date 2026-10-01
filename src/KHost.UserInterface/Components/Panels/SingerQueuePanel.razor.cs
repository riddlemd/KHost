using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components.Panels;

public partial class SingerQueuePanel : IAsyncDisposable
{
    [Inject] private ISingerQueueService SingerQueueService { get; set; } = default!;
    [Inject] private IPerformanceService PerformanceService { get; set; } = default!;
    [Inject] private IMediaService MediaService { get; set; } = default!;
    [Inject] private IPlaybackService PlaybackService { get; set; } = default!;
    [Inject] private IUsersService UsersService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private IPermissionService Permissions { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IVenuesService VenuesService { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    /// <summary>Fired when the add-singer target is queued, not a click on an existing singer.</summary>
    [Parameter] public EventCallback OnSingerAdded { get; set; }

    private readonly SubscriptionSet _subscriptions = new();

    private const int SingerSuggestionLimit = 20;

    /// <summary>Where singers go when no performance of theirs carries a venue at all.</summary>
    private const string NoVenueGroup = "Not sung here before";

    /// <summary>A deleted venue still names a group; it just cannot name itself.</summary>
    private const string UnknownVenueGroup = "Another venue";

    private KHostUser? _pickedSinger;
    private string _newSingerName = string.Empty;
    private List<Performance> _allQueuedPerformances = [];
    private Dictionary<Guid, Media?> _mediaCache = [];
    private Dictionary<Guid, int> _performanceCounts = [];
    private Dictionary<Guid, RecentVenueVisit> _lastVenues = [];
    private Dictionary<Guid, string> _venueNames = [];
    private bool _showEwt = true;
    private bool _promptBeforeRemovingSinger;
    private bool _canAddToQueue;
    private bool _canRemoveFromQueue;
    private bool _canReorderQueue;
    private Guid? _lastScrolledSingerId;
    private SortableBinding _sortable = default!;

    protected override async Task OnInitializedAsync()
    {
        // The row itself drags. Its buttons are filtered out so a press on remove or an arrow
        // does what it says, and the locked row still refuses to move.
        _sortable = new SortableBinding(
            JS, "singers", ".kh-singer-queue-panel__singer-queue",
            "button, .kh-singer-queue-panel__singer-queue__singer--locked", "singerId", nameof(OnSortEndAsync));

        _subscriptions.Add(Broker.Subscribe<SingerQueueChanged>(_ => OnStateChanged()));
        _subscriptions.Add(Broker.Subscribe<PerformancesChanged>(_ => OnStateChanged()));
        _subscriptions.Add(Broker.Subscribe<PlaybackChanged>(_ => OnStateChanged()));
        _subscriptions.Add(Broker.Subscribe<VenuesChanged>(_ => OnStateChanged()));

        var venues = await VenuesService.ReadAllAsync(pageSize: 0);
        _venueNames = venues.Items.ToDictionary(v => v.Id, v => v.Name);

        _canAddToQueue = await Permissions.HasAsync(KHostPermission.AddToQueue);
        _canRemoveFromQueue = await Permissions.HasAsync(KHostPermission.RemoveFromQueue);
        _canReorderQueue = await Permissions.HasAsync(KHostPermission.ReorderQueue);
    }

    protected override async Task OnParametersSetAsync()
    {
        await RefreshPerformanceCountsAsync();
        await RefreshVenueSettingsAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // A truly-async first await in OnInitializedAsync leaves _canReorderQueue still false on
        // firstRender, so attaching has to follow the permission in rather than fire once on it.
        await _sortable.SyncAsync(_canReorderQueue, this);

        // Only on an actual selection change: scrolling on every render yanks the list while
        // the host is scrolling it by hand.
        if (SingerQueueService.SelectedUserId is { } selectedId && selectedId != _lastScrolledSingerId)
        {
            _lastScrolledSingerId = selectedId;
            await ScrollToSelectedSingerAsync();
        }
    }

    [JSInvokable]
    public async Task OnSortEndAsync(string userIdStr, int newIndex)
    {
        if (Guid.TryParse(userIdStr, out var userId))
            await SingerQueueService.MoveUserToIndexAsync(userId, newIndex);
    }

    private async Task<IReadOnlyList<KHostUser>> SearchSingersAsync(string query)
    {
        var result = await UsersService.SearchAsync(
            query, 1, SingerSuggestionLimit, new UserSearchOptions { SingersOnly = true });

        if (result.Items.Count == 0)
            return result.Items;

        _lastVenues = new Dictionary<Guid, RecentVenueVisit>(
            await PerformanceService.ReadLastVenueBySingersAsync(result.Items.Select(u => u.Id)));

        return RankByVenue(result.Items, _lastVenues, VenuesService.SelectedVenueId);
    }

    /// <summary>Orders by venue: current first, recent next, no-venue last; recency within each.</summary>
    public static IReadOnlyList<KHostUser> RankByVenue(
        IReadOnlyList<KHostUser> singers,
        IReadOnlyDictionary<Guid, RecentVenueVisit> lastVenues,
        Guid? currentVenueId)
    {
        Guid? VenueOf(KHostUser singer)
            => lastVenues.TryGetValue(singer.Id, out var visit) ? visit.VenueId : null;

        DateTime LastSungOn(KHostUser singer)
            => lastVenues.TryGetValue(singer.Id, out var visit) ? visit.LastSungOn : DateTime.MinValue;

        return
        [
            .. singers
                .GroupBy(VenueOf)
                .OrderByDescending(group => group.Key is not null && group.Key == currentVenueId)
                .ThenByDescending(group => group.Key is not null)
                .ThenByDescending(group => group.Max(LastSungOn))
                .SelectMany(group => group.OrderByDescending(LastSungOn).ThenBy(singer => singer.Name))
        ];
    }

    /// <summary>Reads the ephemeral flag, not the provider: the mark says tonight, not a plugin.</summary>
    private static bool JoinedFromTheRoom(KHostUser singer)
        => singer.ForeignKeys.Any(key => key.IsEphemeral);

    private string? GroupForSinger(KHostUser singer)
        => _lastVenues.TryGetValue(singer.Id, out var visit)
            ? _venueNames.GetValueOrDefault(visit.VenueId, UnknownVenueGroup)
            : NoVenueGroup;

    private async Task OnSingerPickedAsync(KHostUser? singer)
    {
        _pickedSinger = singer;

        // Choosing a known singer is the whole action. There is nothing left for the button to do,
        // and the text is already theirs because the box sets it before it reports the choice.
        if (singer is not null)
            await AddUserAsync();
    }

    private async Task AddUserAsync()
    {
        if (string.IsNullOrWhiteSpace(_newSingerName)) return;

        var name = _newSingerName.Trim();

        // A singer picked from the list is already the one meant; a name that matches nobody is a
        // new singer. Typing after picking clears the pick, so the two cannot disagree.
        var user = _pickedSinger
            ?? (await UsersService.SearchAsync(name)).Items
                   .FirstOrDefault(u => u.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? await UsersService.CreateAsync(new KHostUser { Name = name });

        await SingerQueueService.AddAndSelectUserAsync(user.Id);

        _pickedSinger = null;
        _newSingerName = string.Empty;

        await OnSingerAdded.InvokeAsync();
    }

    private async Task OnKeyDownAsync(KeyboardEventArgs e)
    {
        var users = SingerQueueService.Users;
        var currentIdx = users.ToList().FindIndex(u => u.Id == SingerQueueService.SelectedUserId);
        var action = ListKeyboardShortcuts.Resolve(
            e.Key, e.ShiftKey, currentIdx, users.Count, modified: e.CtrlKey || e.MetaKey || e.AltKey);

        await ListKeyboardShortcuts.DispatchAsync(
            action, currentIdx, _canReorderQueue,
            select: idx => SingerQueueService.SelectUserAsync(users[idx].Id),
            move: up => up
                ? SingerQueueService.MoveUserUpAsync(users[currentIdx].Id)
                : SingerQueueService.MoveUserDownAsync(users[currentIdx].Id),
            remove: () => RemoveSelectedFromKeyboardAsync(users[currentIdx]));
    }

    /// <summary>The row's remove button with its refusals: no permission, or the singer at the mic.</summary>
    private Task RemoveSelectedFromKeyboardAsync(KHostUser user)
    {
        if (!_canRemoveFromQueue || PlaybackService.CurrentlyPerformingUserId == user.Id)
            return Task.CompletedTask;

        return ConfirmRemoveUserAsync(user, selectNeighbour: true);
    }

    private TimeSpan CalculateEwt(int userIndex)
    {
        if (userIndex == 0) return TimeSpan.Zero;

        var total = TimeSpan.Zero;

        for (var i = 0; i < userIndex; i++)
        {
            var user = SingerQueueService.Users[i];
            var nextPerf = _allQueuedPerformances.FirstOrDefault(p => p.SingerId == user.Id);

            if (nextPerf is not null && _mediaCache.TryGetValue(nextPerf.MediaId, out var media) && media?.Duration is { } duration)
                total += duration;
        }

        return total;
    }

    private string FormatEwt(TimeSpan duration)
        => $"{((int)Math.Floor(duration.TotalMinutes)):D2}:{duration.Seconds:D2}";

    private async Task ScrollToSelectedSingerAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("scrollIntoViewSmooth", ".kh-singer-queue-panel--selected", -10);
        }
        catch { }
    }

    /// <param name="selectNeighbour">Keyboard removal keeps a row selected so the next key press
    /// still has something to act on; the queue clears the selection of a removed singer.</param>
    private Task ConfirmRemoveUserAsync(KHostUser user, bool selectNeighbour = false)
        => DialogService.ConfirmIfAsync(
            _promptBeforeRemovingSinger,
            async () =>
            {
                // Read at confirm time: the queue can move while the dialog is open.
                var users = SingerQueueService.Users;
                var index = users.ToList().FindIndex(u => u.Id == user.Id);
                var neighbour = ListKeyboardShortcuts.NeighbourAfterRemoval(index, users.Count);
                Guid? neighbourId = neighbour >= 0 ? users[neighbour].Id : null;

                await SingerQueueService.RemoveUserAsync(user.Id);

                if (selectNeighbour && neighbourId is { } id)
                    await SingerQueueService.SelectUserAsync(id);
            },
            $"Are you sure you want to remove <span class=\"kh-emphasis\">{user.Name}</span> from the queue?",
            "Remove Singer From Queue",
            "Remove");

    private void OnStateChanged() => InvokeAsync(async () =>
    {
        await RefreshPerformanceCountsAsync();
        await RefreshVenueSettingsAsync();

        StateHasChanged();
    });

    // Rendering needs this synchronously, and the venue read is async, so cache it and refresh
    // on venue state changes so saving the setting takes effect without a reload.
    private async Task RefreshVenueSettingsAsync()
    {
        var venue = await VenuesService.ReadSelectedVenueAsync();

        _showEwt = venue?.Settings.ShowEstimatedWaitTime ?? true;
        _promptBeforeRemovingSinger = venue?.Settings.PromptBeforeRemovingSinger ?? false;
    }

    private string GetSingerRowClasses(Guid userId, bool isFirst)
    {
        var classes = new List<string> { "kh-singer-queue-panel__singer-queue__singer" };

        if (SingerQueueService.SelectedUserId == userId)
            classes.Add("kh-singer-queue-panel__singer-queue__singer--selected");

        if (isFirst && SingerQueueService.IsTopSlotLocked)
            classes.Add("kh-singer-queue-panel__singer-queue__singer--locked");

        return string.Join(" ", classes);
    }

    private async Task RefreshPerformanceCountsAsync()
    {
        _allQueuedPerformances = await PerformanceService.ReadQueuedAsync();

        _performanceCounts.Clear();
        foreach (var user in SingerQueueService.Users)
            _performanceCounts[user.Id] = _allQueuedPerformances.Count(p => p.SingerId == user.Id);

        var nextMediaIds = SingerQueueService.Users
            .Select(u => _allQueuedPerformances.FirstOrDefault(p => p.SingerId == u.Id)?.MediaId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value);

        _mediaCache = await MediaCacheLoader.ReadByIdAsync(MediaService, nextMediaIds);
    }

    public async ValueTask DisposeAsync()
    {
        _subscriptions.Dispose();
        await _sortable.DisposeAsync();
    }
}
