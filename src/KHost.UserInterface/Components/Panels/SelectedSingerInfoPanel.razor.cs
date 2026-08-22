using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.UserInterface.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components.Panels;

public partial class SelectedSingerInfoPanel : IDisposable
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

    private List<Performance> _performances = [];
    private Dictionary<Guid, Media?> _mediaCache = [];
    private int _performanceCount = 0;
    private Guid? _selectedPerformanceId;
    private int _tonightTotalInCents;
    private int _lifetimeTotalInCents;
    private bool _tippingEnabled = true;
    private bool _canRemoveFromQueue;
    private bool _canReorderQueue;
    private bool _canViewHistory;

    protected override async Task OnInitializedAsync()
    {
        SingerQueueService?.StateChanged += OnStateChanged;
        PerformanceService?.StateChanged += OnStateChanged;
        PlaybackService?.StateChanged += OnStateChanged;
        MediaService?.StateChanged += OnStateChanged;
        VenuesService?.StateChanged += OnStateChanged;

        if (Permissions is not null)
        {
            _canRemoveFromQueue = await Permissions.HasAsync(KHostPermission.RemoveFromQueue);
            _canReorderQueue = await Permissions.HasAsync(KHostPermission.ReorderQueue);
            _canViewHistory = await Permissions.HasAsync(KHostPermission.ViewPerformanceHistory);
        }

        await RefreshVenueSettingsAsync();
        await RefreshPerformancesAsync();
    }

    private Task OnKeyDownAsync(KeyboardEventArgs e)
    {
        if (SingerQueueService?.SelectedUser is null) return Task.CompletedTask;

        var currentIdx = _performances.FindIndex(p => p.Id == _selectedPerformanceId);
        var action = ListKeyboardShortcuts.Resolve(e.Key, e.ShiftKey, currentIdx, _performances.Count);

        if (action is ListKeyAction.SelectPrevious)
            SelectPerformance(_performances[currentIdx - 1].Id);
        else if (action is ListKeyAction.SelectNext)
            SelectPerformance(_performances[currentIdx + 1].Id);

        return Task.CompletedTask;
    }

    private async Task LoadAndPlayAsync(Performance performance)
    {
        if (PlaybackService is null) return;

        if (PlaybackService.State == PlaybackState.Playing && PlaybackService.CurrentPerformance?.Id != performance.Id)
            return;

        if (!_mediaCache.TryGetValue(performance.MediaId, out var media) || media is null)
            return;

        // Not just the disabled button: the row's cached media can be a render behind a status
        // change made elsewhere, so refuse the play here too.
        if (media.Status == MediaStatus.Broken)
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

    private void OnStateChanged(object? sender, EventArgs e) => InvokeAsync(async () =>
    {
        await RefreshVenueSettingsAsync();
        await RefreshPerformancesAsync();
        StateHasChanged();
    });

    // Rendering needs this synchronously, and the venue read is async — cache it and refresh
    // on venue state changes so toggling tipping takes effect without a reload.
    private async Task RefreshVenueSettingsAsync()
    {
        if (VenuesService is null) return;

        var venue = await VenuesService.ReadSelectedVenueAsync();

        _tippingEnabled = venue?.Settings.TippingEnabled ?? true;
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

    public void Dispose()
    {
        SingerQueueService?.StateChanged    -= OnStateChanged;
        PerformanceService?.StateChanged    -= OnStateChanged;
        PlaybackService?.StateChanged -= OnStateChanged;
        MediaService?.StateChanged -= OnStateChanged;
        VenuesService?.StateChanged -= OnStateChanged;
    }
}
