using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;
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
    [Inject] private IPermissionService? Permissions { get; set; }
    [Inject] private IJSRuntime? JS { get; set; }
    [Inject] private IVenuesService? VenuesService { get; set; }

    private const int SingerSuggestionLimit = 20;

    /// <summary>Where singers go when no performance of theirs carries a venue at all.</summary>
    private const string NoVenueGroup = "Not sung here before";

    /// <summary>A venue that has since been deleted still names a group; it just cannot name itself.</summary>
    private const string UnknownVenueGroup = "Another venue";

    private KHostUser? _pickedSinger;
    private string _newSingerName = string.Empty;
    private List<Performance> _allQueuedPerformances = [];
    private Dictionary<Guid, Media?> _mediaCache = [];
    private Dictionary<Guid, int> _performanceCounts = [];
    private Dictionary<Guid, RecentVenueVisit> _lastVenues = [];
    private Dictionary<Guid, string> _venueNames = [];
    private DotNetObjectReference<SingerQueuePanel>? _dotNetRef;
    private bool _showEwt = true;
    private bool _canAddToQueue;
    private bool _canRemoveFromQueue;
    private bool _canReorderQueue;

    protected override async Task OnInitializedAsync()
    {
        SingerQueueService?.StateChanged  += OnStateChanged;
        PerformanceService?.StateChanged  += OnStateChanged;
        PlaybackService?.StateChanged += OnStateChanged;
        VenuesService?.StateChanged += OnStateChanged;

        if (VenuesService is not null)
        {
            var venues = await VenuesService.ReadAllAsync(pageSize: 0);
            _venueNames = venues.Items.ToDictionary(v => v.Id, v => v.Name);
        }

        if (Permissions is not null)
        {
            _canAddToQueue = await Permissions.HasAsync(KHostPermission.AddToQueue);
            _canRemoveFromQueue = await Permissions.HasAsync(KHostPermission.RemoveFromQueue);
            _canReorderQueue = await Permissions.HasAsync(KHostPermission.ReorderQueue);
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        await RefreshPerformanceCountsAsync();
        await RefreshVenueSettingsAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await RefreshPerformanceCountsAsync();

        // Sortable would happily reorder for someone the arrows are hidden from.
        if (firstRender && JS is not null && _canReorderQueue)
        {
            _dotNetRef = DotNetObjectReference.Create(this);
            // The name reaches JS as a string; nameof turns a missed rename into a compile
            // error instead of a callback that silently stops firing.
            await JS.InvokeVoidAsync(
                "singerQueueSortable.init",
                ".kh-singer-queue-panel__singer-queue",
                _dotNetRef,
                nameof(OnSortEndAsync));
        }

        if (SingerQueueService?.SelectedUserId is not null)
            await ScrollToSelectedSingerAsync();
    }

    [JSInvokable]
    public async Task OnSortEndAsync(string userIdStr, int newIndex)
    {
        if (Guid.TryParse(userIdStr, out var userId) && SingerQueueService is not null)
            await SingerQueueService.MoveUserToIndexAsync(userId, newIndex);
    }

    private async Task<IReadOnlyList<KHostUser>> SearchSingersAsync(string query)
    {
        if (UsersService is null) return [];

        var result = await UsersService.SearchAsync(
            query, 1, SingerSuggestionLimit, new UserSearchOptions { SingersOnly = true });

        if (PerformanceService is null || result.Items.Count == 0)
            return result.Items;

        _lastVenues = new Dictionary<Guid, RecentVenueVisit>(
            await PerformanceService.ReadLastVenueBySingersAsync(result.Items.Select(u => u.Id)));

        return RankByVenue(result.Items, _lastVenues, VenuesService?.SelectedVenueId);
    }

    /// <summary>
    /// Venue groups, this one first, then by how recently anyone sang at each — and singers with no
    /// venue at all last. Inside a group, most recently sung first: the likeliest to be back.
    /// The combo box labels runs without reordering them, so the grouping is this ordering.
    /// </summary>
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

    private string? GroupForSinger(KHostUser singer)
        => _lastVenues.TryGetValue(singer.Id, out var visit)
            ? _venueNames.GetValueOrDefault(visit.VenueId, UnknownVenueGroup)
            : NoVenueGroup;

    private async Task OnSingerPickedAsync(KHostUser? singer)
    {
        _pickedSinger = singer;

        // Choosing a known singer is the whole action — there is nothing left for the button to do,
        // and the text is already theirs because the box sets it before it reports the choice.
        if (singer is not null)
            await AddUserAsync();
    }

    private async Task AddUserAsync()
    {
        if (string.IsNullOrWhiteSpace(_newSingerName)) return;
        if (SingerQueueService is null || UsersService is null) return;

        var name = _newSingerName.Trim();

        // A singer picked from the list is already the one meant; a name that matches nobody is a
        // new singer. Typing after picking clears the pick, so the two cannot disagree.
        var user = _pickedSinger
            ?? (await UsersService.SearchAsync(name)).Items
                   .FirstOrDefault(u => u.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? await UsersService.CreateAsync(new KHostUser { Name = name });

        await SingerQueueService.AddUserAsync(user.Id);
        await SingerQueueService.SelectUserAsync(user.Id);

        _pickedSinger = null;
        _newSingerName = string.Empty;
    }

    private async Task OnKeyDownAsync(KeyboardEventArgs e)
    {
        if (SingerQueueService is null) return;

        var users = SingerQueueService.Users;
        var currentIdx = users.ToList().FindIndex(u => u.Id == SingerQueueService.SelectedUserId);
        var action = ListKeyboardShortcuts.Resolve(e.Key, e.ShiftKey, currentIdx, users.Count);

        // Reordering is a permission of its own; the arrows that do it are hidden without it.
        if (action is ListKeyAction.MovePrevious or ListKeyAction.MoveNext && !_canReorderQueue)
            return;

        switch (action)
        {
            case ListKeyAction.SelectPrevious:
                await SingerQueueService.SelectUserAsync(users[currentIdx - 1].Id);
                break;
            case ListKeyAction.SelectNext:
                await SingerQueueService.SelectUserAsync(users[currentIdx + 1].Id);
                break;
            case ListKeyAction.MovePrevious:
                await SingerQueueService.MoveUserUpAsync(users[currentIdx].Id);
                break;
            case ListKeyAction.MoveNext:
                await SingerQueueService.MoveUserDownAsync(users[currentIdx].Id);
                break;
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
