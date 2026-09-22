using System.Globalization;
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
    [Inject] private ISingerQueueService? SingerQueueService { get; set; }
    [Inject] private IPerformanceService? PerformanceService { get; set; }
    [Inject] private IPlaybackService? PlaybackService { get; set; }
    [Inject] private IMediaSearchService? MediaSearchService { get; set; }
    [Inject] private IUsersService? UsersService { get; set; }
    [Inject] private IUserGroupsService? UserGroupsService { get; set; }
    [Inject] private IMediaService? MediaService { get; set; }
    [Inject] private IDialogService? DialogService { get; set; }
    [Inject] private IPermissionService? Permissions { get; set; }
    [Inject] private ITipsService? TipsService { get; set; }
    [Inject] private IVenuesService? VenuesService { get; set; }
    [Inject] private IJSRuntime? JS { get; set; }
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private List<Performance> _performances = [];
    private Dictionary<Guid, Media?> _mediaCache = [];
    private int _performanceCount = 0;
    private Guid? _selectedPerformanceId;
    private DotNetObjectReference<SelectedSingerInfoPanel>? _dotNetRef;
    private bool _sortableAttached;
    private int _tonightTotalInCents;
    private int _lifetimeTotalInCents;
    private bool _tippingEnabled = true;

    /// <summary>Off for a venue never asked, so an alias stays out of sight until one is wanted.</summary>
    private bool _allowAliases;
    private bool _canRemoveFromQueue;
    private bool _canReorderQueue;
    private bool _canViewHistory;

    protected override async Task OnInitializedAsync()
    {
        _subscriptions.Add(Broker.Subscribe<SingerQueueChanged>(_ => OnStateChanged()));
        _subscriptions.Add(Broker.Subscribe<PerformancesChanged>(_ => OnStateChanged()));
        _subscriptions.Add(Broker.Subscribe<PlaybackChanged>(_ => OnStateChanged()));
        _subscriptions.Add(Broker.Subscribe<MediaLibraryChanged>(_ => OnStateChanged()));
        _subscriptions.Add(Broker.Subscribe<VenuesChanged>(_ => OnStateChanged()));

        if (Permissions is not null)
        {
            _canRemoveFromQueue = await Permissions.HasAsync(KHostPermission.RemoveFromQueue);
            _canReorderQueue = await Permissions.HasAsync(KHostPermission.ReorderQueue);
            _canViewHistory = await Permissions.HasAsync(KHostPermission.ViewPerformanceHistory);
        }

        await RefreshVenueSettingsAsync();
        await RefreshPerformancesAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Unlike the singer queue's, this table is absent until the singer has a song, so the
        // hook-up cannot be a first-render one-shot: it has to follow the table in and out.
        var sortable = _canReorderQueue && _performances.Count > 0;

        if (sortable == _sortableAttached || JS is null) return;

        if (sortable)
        {
            _dotNetRef ??= DotNetObjectReference.Create(this);

            // The method name reaches JS as a string; nameof turns a missed rename into a compile
            // error instead of a callback that silently stops firing.
            await JS.InvokeVoidAsync(
                "khSortable.init",
                "songs",
                ".kh-selected-singer-info-panel__rows",
                // The row itself drags. Its buttons are filtered out, or a press on play or
                // remove would start a drag instead of doing what it says.
                null,
                "button",
                _dotNetRef,
                nameof(OnSortEndAsync),
                "performanceId");
        }
        else
        {
            await JS.InvokeVoidAsync("khSortable.destroy", "songs");
        }

        _sortableAttached = sortable;
    }

    [JSInvokable]
    public async Task OnSortEndAsync(string performanceIdStr, int newIndex)
    {
        if (SingerQueueService?.SelectedUser is not { } singer) return;
        if (!Guid.TryParse(performanceIdStr, out var performanceId)) return;

        await (PerformanceService?.MoveToIndexAsync(singer.Id, performanceId, newIndex) ?? Task.CompletedTask);
    }

    private async Task OnKeyDownAsync(KeyboardEventArgs e)
    {
        if (SingerQueueService?.SelectedUser is not { } singer) return;

        var currentIdx = _performances.FindIndex(p => p.Id == _selectedPerformanceId);
        var action = ListKeyboardShortcuts.Resolve(e.Key, e.ShiftKey, currentIdx, _performances.Count);

        // Reordering is a permission of its own; the arrows that do it are hidden without it.
        if (action is ListKeyAction.MovePrevious or ListKeyAction.MoveNext && !_canReorderQueue)
            return;

        switch (action)
        {
            case ListKeyAction.SelectPrevious:
                SelectPerformance(_performances[currentIdx - 1].Id);
                break;
            case ListKeyAction.SelectNext:
                SelectPerformance(_performances[currentIdx + 1].Id);
                break;
            case ListKeyAction.MovePrevious:
                await (PerformanceService?.MoveUpInQueueAsync(singer.Id, _performances[currentIdx].Id) ?? Task.CompletedTask);
                break;
            case ListKeyAction.MoveNext:
                await (PerformanceService?.MoveDownInQueueAsync(singer.Id, _performances[currentIdx].Id) ?? Task.CompletedTask);
                break;
        }
    }

    private async Task LoadAndPlayAsync(Performance performance)
    {
        if (PlaybackService is null) return;

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
            await DialogService!.ShowNoScreensAsync();
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
            await DialogService!.ShowErrorAsync(
                ex,
                title: "Couldn't start this song",
                onRetry: () => _ = LoadAndPlayAsync(performance));
        }
    }

    private async Task OpenPerformanceHistoryDialogAsync()
    {
        if (SingerQueueService?.SelectedUser is null) return;
        await DialogService!.ShowSingerPerformanceHistoryAsync(SingerQueueService.SelectedUser.Id);
    }

    private async Task OpenUserEditDialogAsync()
    {
        if (SingerQueueService?.SelectedUser is null) return;
        await DialogService!.RequestEditAsync(SingerQueueService.SelectedUser, async user => await SaveUserAsync(user));
    }

    private async Task SaveUserAsync(KHostUser? user)
    {
        if (SingerQueueService is null || UsersService is null || user is null) return;
        await UsersService.UpdateAsync(user);
        await SingerQueueService.RefreshAsync();
    }

    private async Task OpenAddTipDialogAsync()
    {
        if (SingerQueueService?.SelectedUser is not { } user) return;
        await DialogService!.RequestEditAsync(null, user.Id, showDate: false, onSave: async savedTip =>
        {
            if (savedTip is null) return;
            await TipsService!.CreateAsync(savedTip);
            await RefreshPerformancesAsync();
            StateHasChanged();
        });
    }

    private async Task RemoveWithConfirmAsync(Performance performance)
    {
        if (PerformanceService is null) return;

        var venue = VenuesService is not null ? await VenuesService.ReadSelectedVenueAsync() : null;
        if (venue?.Settings.PromptBeforeRemovingPerformance == true)
        {
            if (DialogService is null) return;

            await DialogService.ShowConfirmationAsync(
                $"Are you sure you want to remove this song from the queue?",
                () => PerformanceService.DeleteAsync(performance.Id),
                title: "Remove Song",
                confirmText: "Remove"
            );
        }
        else
        {
            await PerformanceService.DeleteAsync(performance.Id);
        }
    }

    private async Task OpenMediaEditDialogAsync(Media? media)
    {
        if (media is null || MediaService is null || DialogService is null) return;
        await DialogService.RequestEditAsync(media, async updated =>
        {
            if (updated is not null)
                await MediaService.UpdateAsync(updated);
        });
    }

    /// <summary>The turn's name, not the singer's: saves the performance, not the account.</summary>
    private async Task OpenSingingAsDialogAsync(Performance performance, KHostUser singer)
    {
        if (PerformanceService is null || DialogService is null) return;

        await DialogService.RequestSingingAsAsync(performance, singer.Name, async updated =>
        {
            if (updated is not null)
                await PerformanceService.UpdateAsync(updated);
        });
    }

    private async Task ToggleIsRegularAsync()
    {
        if (SingerQueueService?.SelectedUser is { } user && UserGroupsService is not null)
        {
            var isRegular = await UserGroupsService.IsUserInGroupAsync(user.Id, KHostUserGroup.RegularGroupId);
            if (isRegular)
                await UserGroupsService.RemoveUserFromGroupAsync(user.Id, KHostUserGroup.RegularGroupId);
            else
                await UserGroupsService.AddUserToGroupAsync(user.Id, KHostUserGroup.RegularGroupId);
            await SingerQueueService.RefreshAsync();
        }
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
        if (VenuesService is null) return;

        var venue = await VenuesService.ReadSelectedVenueAsync();

        _tippingEnabled = venue?.Settings.TippingEnabled ?? true;
        _allowAliases = venue?.Settings.AllowAliases ?? false;
    }

    private async Task RefreshPerformancesAsync()
    {
        if (SingerQueueService is null) return;
        if (PerformanceService is null) return;

        if (SingerQueueService.SelectedUser is { } user)
        {
            _performances = (await PerformanceService.ReadQueuedAsync()).Where(p => p.SingerId == user.Id).ToList();
            _performanceCount = _performances.Count;
            if (!_performances.Any(p => p.Id == _selectedPerformanceId))
                _selectedPerformanceId = null;

            if (_performances.Count > 0 && MediaService is not null)
            {
                var mediaIds = _performances.Select(p => p.MediaId).Distinct().ToList();
                var mediaTasks = mediaIds.Select(id => MediaService.ReadAsync(id)).ToList();
                var mediaResults = await Task.WhenAll(mediaTasks);
                _mediaCache = mediaIds.Zip(mediaResults).ToDictionary(x => x.First, x => x.Second);
            }
            else
            {
                _mediaCache = [];
            }

            if (_tippingEnabled && TipsService is not null)
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
            _performanceCount = 0;
            _selectedPerformanceId = null;
            _mediaCache = [];
            _tonightTotalInCents = 0;
            _lifetimeTotalInCents = 0;
        }
    }

    private static string FormatPitch(int semitones) =>
        semitones.ToString("+#;\u2212#;0", CultureInfo.InvariantCulture);

    private static string FormatTempo(int tempo) =>
        tempo.ToString("+#;\u2212#;0", CultureInfo.InvariantCulture) + "%";

    /// <summary>Whether this turn was queued under a name other than the singer's own.</summary>
    /// <remarks>Compares names, not SungAs presence: that field is filled by default on every row.</remarks>
    private static bool SungUnderAnotherName(Performance performance, KHostUser singer)
    {
        var recorded = performance.SungAs?.Trim();

        return !string.IsNullOrEmpty(recorded)
            && !string.Equals(recorded, singer.Name?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Loaded, not merely playing: playback resolves the name once at load, so renaming a
    /// paused song changes nothing on screen while still telling a host it did.</summary>
    private bool AliasLocked(Performance performance)
        => PlaybackService?.CurrentPerformance?.Id == performance.Id;

    /// <summary>Disabled alone reads as broken, so the button has to say which of the two it is.
    /// </summary>
    private string AliasTooltip(Performance performance)
        => AliasLocked(performance)
            ? "This song is at the microphone. Its name was announced when it started."
            : "Change the name this song is announced under";

    /// <summary>Async: tearing the sortable down is a JS call, and the circuit is usually gone.</summary>
    public async ValueTask DisposeAsync()
    {
        _subscriptions.Dispose();
        _dotNetRef?.Dispose();

        try
        {
            if (JS is not null)
                await JS.InvokeVoidAsync("khSortable.destroy", "songs");
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
