using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

public partial class SingerPerformanceHistoryDialog
{
    private const string _rootClassName = "kh-singer-performance-history-dialog";
    private const int _pageSize = 5;

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public Guid UserId { get; set; }
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public bool CloseOnScrimClick { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

    [Inject] private IPerformanceService? PerformanceService { get; set; }
    [Inject] private IMediaService? MediaService { get; set; }
    [Inject] private IDialogService? DialogService { get; set; }
    [Inject] private IVenuesService? VenuesService { get; set; }

    private PaginatedResult<Performance>? _paginatedPerformances;
    private List<Media> _media = [];
    private int _currentPage = 1;
    private bool _prevIsOpen;

    private int TotalPages => _paginatedPerformances?.TotalPages ?? 0;

    protected override async Task OnParametersSetAsync()
    {
        if (IsOpen && !_prevIsOpen)
        {
            _currentPage = 1;
            await LoadPageAsync();
        }
        _prevIsOpen = IsOpen;
    }

    private async Task PreviousPage()
    {
        if (_currentPage > 1)
        {
            _currentPage--;
            await LoadPageAsync();
        }
    }

    private async Task NextPage()
    {
        if (_paginatedPerformances?.HasNextPage ?? false)
        {
            _currentPage++;
            await LoadPageAsync();
        }
    }

    private async Task LoadPageAsync()
    {
        if (PerformanceService is null || MediaService is null)
            return;

        _paginatedPerformances = await PerformanceService.ReadBySingerIdAsync(UserId, pageNumber: _currentPage, pageSize: _pageSize, PerformanceFilter.UnQueued);

        var mediaIds = _paginatedPerformances.Items.Select(p => p.MediaId).Distinct().ToList();
        var mediaResults = await Task.WhenAll(mediaIds.Select(id => MediaService.ReadAsync(id)));

        _media = mediaResults.Where(m => m is not null).Cast<Media>().ToList();

        StateHasChanged();
    }

    public async Task CloseAsync()
    {
        IsOpen = false;

        await OnClose.InvokeAsync();
    }

    private async Task EnqueueAsync(Media media)
    {
        if (PerformanceService is null)
            return;

        var enqueued = await PerformanceService.CreateAndEnqueueAsync(new Performance
        {
            SingerId = UserId,
            MediaId = media.Id
        });

        // Stay open when the duplicate warning was declined, so the choice isn't lost.
        if (enqueued is not null)
            await CloseAsync();
    }

    private async Task EditAsync(Media media)
    {
        if (DialogService is null) return;

        await DialogService.RequestEditAsync(media, async (media) => await SaveMedia(media));
    }

    // Always confirmed: history is not recoverable from anywhere else in the app.
    private async Task ConfirmDeleteAsync(Guid performanceId)
    {
        if (DialogService is null) return;

        await DialogService.ShowConfirmationAsync("Are you sure you want to delete this <span class=\"kh-emphasis\">performance</span> from the user's history?", async () =>
        {
            await DeleteAsync(performanceId);
        },
        "Delete Performance",
        "Delete");
    }

    private async Task DeleteAsync(Guid performanceId)
    {
        if (PerformanceService is null)
            return;

        await PerformanceService.DeleteAsync(performanceId);

        await LoadPageAsync();
    }

    private async Task SaveMedia(Media? media)
    {
        if (MediaService is null) return;
        if (media is null) return;

        await MediaService.UpdateAsync(media);

        await LoadPageAsync();
    }

    public record DialogRequest : BaseDialogRequest
    {
        public DialogRequest(Guid userId, Action? OnClose) : base(OnClose)
        {
            UserId = userId;
        }

        public Guid UserId { get; init; }
    }
}
