using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components.Panels;

public partial class SingerQueuePanel : IDisposable
{
    [Inject] private ISingerQueueService? SingerQueueService { get; set; }
    [Inject] private IPerformanceService? PerformanceService { get; set; }
    [Inject] private IMediaService? MediaService { get; set; }
    [Inject] private IPlaybackService? PlaybackService { get; set; }
    [Inject] private IUsersService? UsersService { get; set; }
    [Inject] private IDialogService? DialogService { get; set; }
    [Inject] private IJSRuntime? JS { get; set; }
    [Inject] private IVenuesService? VenuesService { get; set; }

    private string _newSingerName = string.Empty;
    private List<Performance> _allQueuedPerformances = [];
    private Dictionary<Guid, Media?> _mediaCache = [];
    private Dictionary<Guid, int> _performanceCounts = [];
    private DotNetObjectReference<SingerQueuePanel>? _dotNetRef;
    private bool _showEwt = true;

    protected override void OnInitialized()
    {
        SingerQueueService?.StateChanged  += OnStateChanged;
        PerformanceService?.StateChanged  += OnStateChanged;
        PlaybackService?.StateChanged += OnStateChanged;
        VenuesService?.StateChanged += OnStateChanged;
    }

    protected override async Task OnParametersSetAsync()
    {
        await RefreshPerformanceCountsAsync();
        await RefreshVenueSettingsAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await RefreshPerformanceCountsAsync();

        if (firstRender && JS is not null)
        {
            _dotNetRef = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync(
                "singerQueueSortable.init",
                ".kh-singer-queue-panel__singer-queue",
                _dotNetRef);
        }

        if (SingerQueueService?.SelectedUserId is not null)
            await ScrollToSelectedSingerAsync();
    }

    [JSInvokable]
    public async Task OnSortEnd(string userIdStr, int newIndex)
    {
        if (Guid.TryParse(userIdStr, out var userId) && SingerQueueService is not null)
            await SingerQueueService.MoveUserToIndexAsync(userId, newIndex);
    }

    private async Task AddUserAsync()
    {
        if (string.IsNullOrWhiteSpace(_newSingerName)) return;
        if (SingerQueueService is null || UsersService is null) return;

        var name = _newSingerName.Trim();
        var results = await UsersService.SearchAsync(name);
        var user = results.Items.FirstOrDefault(u => u.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                     ?? await UsersService.CreateAsync(new KHostUser { Name = name });

        await SingerQueueService.AddUserAsync(user.Id);
        await SingerQueueService.SelectUserAsync(user.Id);

        _newSingerName = string.Empty;
    }

    private async Task OnKeyDownAsync(KeyboardEventArgs e)
    {
        if (SingerQueueService is null) return;

        var selectedUserId = SingerQueueService.SelectedUserId;
        var currentIdx = SingerQueueService.Users.ToList().FindIndex(u => u.Id == SingerQueueService.SelectedUserId);

        if (e.ShiftKey && selectedUserId != null)
        {
            if (e.Key == "ArrowUp" && currentIdx > 0)
                await SingerQueueService.MoveUserUpAsync(SingerQueueService.SelectedUserId!.Value);
            else if (e.Key == "ArrowDown" && currentIdx < SingerQueueService.Users.Count - 1)
                await SingerQueueService.MoveUserDownAsync(SingerQueueService.SelectedUserId!.Value);
        }
        else
        {
            KHostUser? selectedUser = null;

            if (e.Key == "ArrowUp" && currentIdx > 0)
                selectedUser = SingerQueueService.Users[currentIdx - 1];
            else if (e.Key == "ArrowDown" && currentIdx < SingerQueueService.Users.Count - 1)
                selectedUser = SingerQueueService.Users[currentIdx + 1];

            if(selectedUser != null)
                await SingerQueueService.SelectUserAsync(selectedUser.Id);
        }
    }

    private TimeSpan CalculateEwt(int userIndex)
    {
        if (userIndex == 0 || SingerQueueService is null) return TimeSpan.Zero;

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
        if (JS is null) return;

        try
        {
            await JS.InvokeVoidAsync("scrollIntoViewSmooth", ".kh-singer-queue-panel--selected", -10);
        }
        catch { }
    }

    private async Task ConfirmRemoveUserAsync(KHostUser user)
    {
        if (SingerQueueService is null) return;

        var venue = VenuesService is not null ? await VenuesService.ReadSelectedVenueAsync() : null;
        if (venue?.Settings.PromptBeforeRemovingSinger == true)
        {
            if (DialogService is null) return;

            bool confirmed = await DialogService.ShowConfirmationAsync(
                $"Are you sure you want to remove <span class=\"kh-emphasis\">{user.Name}</span> from the queue?",
                async () => await SingerQueueService.RemoveUserAsync(user.Id),
                "Remove Singer From Queue",
                "Remove"
            );
        }
        else
        {
            await SingerQueueService.RemoveUserAsync(user.Id);
        }
    }

    private void OnStateChanged(object? sender, EventArgs e) => InvokeAsync(async () =>
    {
        await RefreshPerformanceCountsAsync();
        await RefreshVenueSettingsAsync();

        StateHasChanged();
    });

    // Rendering needs this synchronously, and the venue read is async — cache it and refresh
    // on venue state changes so saving the setting takes effect without a reload.
    private async Task RefreshVenueSettingsAsync()
    {
        if (VenuesService is null) return;

        var venue = await VenuesService.ReadSelectedVenueAsync();

        _showEwt = venue?.Settings.ShowEstimatedWaitTime ?? true;
    }

    private string GetSingerRowClasses(Guid userId, bool isFirst)
    {
        var classes = new List<string> { "kh-singer-queue-panel__singer-queue__singer" };

        if (SingerQueueService?.SelectedUserId == userId)
            classes.Add("kh-singer-queue-panel__singer-queue__singer--selected");

        if (isFirst && SingerQueueService?.IsTopSlotLocked == true)
            classes.Add("kh-singer-queue-panel__singer-queue__singer--locked");

        return string.Join(" ", classes);
    }

    private async Task RefreshPerformanceCountsAsync()
    {
        if (SingerQueueService?.Users is null || PerformanceService is null || MediaService is null) return;

        _allQueuedPerformances = await PerformanceService.ReadQueuedAsync();

        _performanceCounts.Clear();
        foreach (var user in SingerQueueService.Users)
            _performanceCounts[user.Id] = _allQueuedPerformances.Count(p => p.SingerId == user.Id);

        var nextMediaIds = SingerQueueService.Users
            .Select(u => _allQueuedPerformances.FirstOrDefault(p => p.SingerId == u.Id)?.MediaId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var mediaResults = await Task.WhenAll(nextMediaIds.Select(id => MediaService.ReadAsync(id)));

        _mediaCache = mediaResults
            .Where(m => m is not null)
            .ToDictionary(m => m!.Id);
    }

    public void Dispose()
    {
        SingerQueueService?.StateChanged -= OnStateChanged;
        PerformanceService?.StateChanged -= OnStateChanged;
        PlaybackService?.StateChanged -= OnStateChanged;
        VenuesService?.StateChanged -= OnStateChanged;
        _dotNetRef?.Dispose();
        JS?.InvokeVoidAsync("singerQueueSortable.destroy");
    }
}
