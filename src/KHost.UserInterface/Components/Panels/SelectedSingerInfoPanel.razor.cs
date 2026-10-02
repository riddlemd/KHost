using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.UserInterface.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components.Panels;

public partial class SelectedSingerInfoPanel : IAsyncDisposable
{
    [Inject] private ISingerQueueService SingerQueueService { get; set; } = default!;
    [Inject] private IPerformanceService PerformanceService { get; set; } = default!;
    [Inject] private IPlaybackService PlaybackService { get; set; } = default!;
    [Inject] private IMediaSearchService MediaSearchService { get; set; } = default!;
    [Inject] private IUsersService UsersService { get; set; } = default!;
    [Inject] private IUserGroupsService UserGroupsService { get; set; } = default!;
    [Inject] private IMediaService MediaService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private IPermissionService Permissions { get; set; } = default!;
    [Inject] private ITipsService TipsService { get; set; } = default!;
    [Inject] private IVenuesService VenuesService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private List<Performance> _performances = [];
    private Dictionary<Guid, Media?> _mediaCache = [];
    private Guid? _selectedPerformanceId;
    private SortableBinding _sortable = default!;
    private int _tonightTotalInCents;
    private int _lifetimeTotalInCents;
    private bool _tippingEnabled = true;

    /// <summary>Off for a venue never asked, so an alias stays out of sight until one is wanted.</summary>
    private bool _allowAliases;
    private bool _promptBeforeRemovingPerformance;
    private bool _canRemoveFromQueue;
    private bool _canReorderQueue;
    private bool _canViewHistory;

    private int _performanceCount => _performances.Count;

    protected override async Task OnInitializedAsync()
    {
        // The row itself drags. Its buttons are filtered out, or a press on play or remove would
        // start a drag instead of doing what it says.
        _sortable = new SortableBinding(
            JS, "songs", ".kh-selected-singer-info-panel__rows", "button", "performanceId", nameof(OnSortEndAsync));

        _subscriptions.Add(Broker.Subscribe<SingerQueueChanged>(_ => OnStateChanged()));
        _subscriptions.Add(Broker.Subscribe<PerformancesChanged>(_ => OnStateChanged()));
        _subscriptions.Add(Broker.Subscribe<PlaybackChanged>(_ => OnStateChanged()));
        _subscriptions.Add(Broker.Subscribe<MediaLibraryChanged>(_ => OnStateChanged()));
        _subscriptions.Add(Broker.Subscribe<VenuesChanged>(_ => OnStateChanged()));

        _canRemoveFromQueue = await Permissions.HasAsync(KHostPermission.RemoveFromQueue);
        _canReorderQueue = await Permissions.HasAsync(KHostPermission.ReorderQueue);
        _canViewHistory = await Permissions.HasAsync(KHostPermission.ViewPerformanceHistory);

        await RefreshVenueSettingsAsync();
        await RefreshPerformancesAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Unlike the singer queue's, this table is absent until the singer has a song, so the
        // hook-up cannot be a first-render one-shot: it has to follow the table in and out.
        await _sortable.SyncAsync(_canReorderQueue && _performances.Count > 0, this);
    }

    [JSInvokable]
    public async Task OnSortEndAsync(string performanceIdStr, int newIndex)
    {
        if (SingerQueueService.SelectedUser is not { } singer) return;
        if (!Guid.TryParse(performanceIdStr, out var performanceId)) return;

        await PerformanceService.MoveToIndexAsync(singer.Id, performanceId, newIndex);
    }

    private Task OnKeyDownAsync(KeyboardEventArgs e)
    {
        if (SingerQueueService.SelectedUser is not { } singer) return Task.CompletedTask;

        var currentIdx = _performances.FindIndex(p => p.Id == _selectedPerformanceId);
        var action = ListKeyboardShortcuts.Resolve(
            e.Key, e.ShiftKey, currentIdx, _performances.Count, modified: e.CtrlKey || e.MetaKey || e.AltKey);

        return ListKeyboardShortcuts.DispatchAsync(
            action, currentIdx, _canReorderQueue,
            select: idx =>
            {
                SelectPerformance(_performances[idx].Id);
                return Task.CompletedTask;
            },
            move: up => up
                ? PerformanceService.MoveUpInQueueAsync(singer.Id, _performances[currentIdx].Id)
                : PerformanceService.MoveDownInQueueAsync(singer.Id, _performances[currentIdx].Id),
            remove: () => RemoveSelectedFromKeyboardAsync(_performances[currentIdx]));
    }

    /// <summary>The row's remove button with its refusals: no permission, or the loaded song.</summary>
    private Task RemoveSelectedFromKeyboardAsync(Performance performance)
    {
        if (!_canRemoveFromQueue || PlaybackService.CurrentPerformance?.Id == performance.Id)
            return Task.CompletedTask;

        return RemoveWithConfirmAsync(performance, selectNeighbour: true);
    }

    private async Task LoadAndPlayAsync(Performance performance)
    {
        // An ad is nobody's turn, so it never matches this performance and would trip the guard
        // below for every row on the panel. A host has to be able to take the room back from one.
        if (PlaybackService.State == PlaybackState.Playing
            && !PlaybackService.IsPlayingAd
            && PlaybackService.CurrentPerformance?.Id != performance.Id)
            return;

        if (!_mediaCache.TryGetValue(performance.MediaId, out var media) || media is null)
            return;

        // Not just the disabled button: cached media can be a render behind a status change.
        // LoadAsync guards too, but a no-op falls through to PlayAsync, resuming the old performance.
        if (media.Status != MediaStatus.Ready)
            return;

        // Before Load, which already moves the singer to the top and locks the slot.
        if (!await PlaybackService.HasConnectedScreenAsync())
        {
            await DialogService.ShowNoScreensAsync();
            return;
        }

        try
        {
            await PlaybackService.LoadAsync(performance, media);
            await PlaybackService.PlayAsync();
        }
        catch (KHostException ex)
        {
            // Nothing reached the screens, so the host has to be told rather than left watching a
            // queue that looks like it started.
            await DialogService.ShowErrorAsync(
                ex,
                title: "Couldn't start this song",
                onRetry: () => _ = LoadAndPlayAsync(performance));
        }
    }

    private async Task OpenPerformanceHistoryDialogAsync()
    {
        if (SingerQueueService.SelectedUser is null) return;
        await DialogService.ShowSingerPerformanceHistoryAsync(SingerQueueService.SelectedUser.Id);
    }

    private async Task OpenUserEditDialogAsync()
    {
        // Re-read rather than the queue's cached copy: a save replaces group memberships with what
        // the dialog holds, and the dialog edits the object it is handed in place.
        if (SingerQueueService.SelectedUser is not { } selected) return;
        if (await UsersService.ReadAsync(selected.Id) is not { } fresh) return;
        await DialogService.RequestEditAsync(fresh, async user => await SaveUserAsync(user));
    }

    private async Task SaveUserAsync(KHostUser? user)
    {
        if (user is null) return;
        await UsersService.UpdateAsync(user);
        await SingerQueueService.RefreshAsync();
    }

    private async Task OpenAddTipDialogAsync()
    {
        if (SingerQueueService.SelectedUser is not { } user) return;
        await DialogService.RequestEditAsync(null, user.Id, showDate: false, onSave: async savedTip =>
        {
            if (savedTip is null) return;
            await TipsService.CreateAsync(savedTip);
            await RefreshPerformancesAsync();
            StateHasChanged();
        });
    }

    /// <param name="selectNeighbour">Keyboard removal keeps a row selected so the next key press
    /// still has something to act on.</param>
    private Task RemoveWithConfirmAsync(Performance performance, bool selectNeighbour = false)
        => DialogService.ConfirmIfAsync(
            _promptBeforeRemovingPerformance,
            async () =>
            {
                // Read at confirm time: the list can refresh while the dialog is open.
                var index = _performances.FindIndex(p => p.Id == performance.Id);
                var neighbour = ListKeyboardShortcuts.NeighbourAfterRemoval(index, _performances.Count);
                Guid? neighbourId = neighbour >= 0 ? _performances[neighbour].Id : null;

                await PerformanceService.DeleteAsync(performance.Id);

                if (selectNeighbour && neighbourId is { } id)
                    await InvokeAsync(() => SelectPerformance(id));
            },
            "Are you sure you want to remove this song from the queue?",
            "Remove Song",
            "Remove");

    private async Task OpenMediaEditDialogAsync(Media? media)
    {
        if (media is null) return;
        await DialogService.RequestEditAsync(media, async updated =>
        {
            if (updated is not null)
                await MediaService.UpdateAsync(updated);
        });
    }

    /// <summary>Opens for the loaded turn too, read-only: the dialog itself says why nothing in it
    /// can change, which a disabled menu item could only put in a tooltip.</summary>
    private async Task OpenEditPerformanceDialogAsync(Performance performance, Media? media, KHostUser singer)
    {
        await DialogService.RequestEditPerformanceAsync(performance, media, singer.Name,
            edit => edit.SaveAsync(PerformanceService, performance.Id));
    }

    private string EditPerformanceTooltip(Performance performance)
        => PlaybackService.CurrentPerformance?.Id == performance.Id
            ? "This song is loaded. Change it with the song controls while it plays."
            : "Change the name, key, tempo and levels this song will be sung at";

    private async Task ToggleIsRegularAsync()
    {
        if (SingerQueueService.SelectedUser is not { } user) return;

        var isRegular = await UserGroupsService.IsUserInGroupAsync(user.Id, KHostUserGroup.RegularGroupId);
        if (isRegular)
            await UserGroupsService.RemoveUserFromGroupAsync(user.Id, KHostUserGroup.RegularGroupId);
        else
            await UserGroupsService.AddUserToGroupAsync(user.Id, KHostUserGroup.RegularGroupId);
        await SingerQueueService.RefreshAsync();
    }

    private void SelectPerformance(Guid performanceId)
    {
        _selectedPerformanceId = performanceId;
        StateHasChanged();
    }

    private void OnStateChanged() => InvokeAsync(async () =>
    {
        await RefreshVenueSettingsAsync();
        await RefreshPerformancesAsync();
        StateHasChanged();
    });

    // Rendering needs this synchronously, and the venue read is async, so cache it and refresh
    // on venue state changes so toggling tipping takes effect without a reload.
    private async Task RefreshVenueSettingsAsync()
    {
        var venue = await VenuesService.ReadSelectedVenueAsync();

        _tippingEnabled = venue?.Settings.TippingEnabled ?? true;
        _allowAliases = venue?.Settings.AllowAliases ?? false;
        _promptBeforeRemovingPerformance = venue?.Settings.PromptBeforeRemovingPerformance ?? false;
    }

    private async Task RefreshPerformancesAsync()
    {
        if (SingerQueueService.SelectedUser is { } user)
        {
            _performances = (await PerformanceService.ReadQueuedAsync()).Where(p => p.SingerId == user.Id).ToList();
            if (!_performances.Any(p => p.Id == _selectedPerformanceId))
                _selectedPerformanceId = null;

            _mediaCache = _performances.Count > 0
                ? await MediaCacheLoader.ReadByIdAsync(MediaService, _performances.Select(p => p.MediaId))
                : [];

            if (_tippingEnabled)
            {
                var tips = await TipsService.GetByUserIdAsync(user.Id);
                _lifetimeTotalInCents = tips.Sum(t => t.AmountInCents);
                _tonightTotalInCents = tips.Where(t => t.CreatedDate.ToLocalTime().Date == DateTime.Now.Date)
                                           .Sum(t => t.AmountInCents);
            }
            else
            {
                _tonightTotalInCents = 0;
                _lifetimeTotalInCents = 0;
            }
        }
        else
        {
            _performances = [];
            _selectedPerformanceId = null;
            _mediaCache = [];
            _tonightTotalInCents = 0;
            _lifetimeTotalInCents = 0;
        }
    }

    /// <summary>Whether this turn was queued under a name other than the singer's own.</summary>
    /// <remarks>Compares names, not SungAs presence: that field is filled by default on every row.</remarks>
    private static bool SungUnderAnotherName(Performance performance, KHostUser singer)
    {
        var recorded = performance.SungAs?.Trim();

        return !string.IsNullOrEmpty(recorded)
            && !string.Equals(recorded, singer.Name?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public async ValueTask DisposeAsync()
    {
        _subscriptions.Dispose();
        await _sortable.DisposeAsync();
    }
}
