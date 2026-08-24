using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class DownloadsManagerPage : IDisposable
{
    [Inject] private IDownloadsService? DownloadsService { get; set; }
    [Inject] private IPerformanceService? PerformanceService { get; set; }

    private List<DownloadInfo> _active = [];
    private List<DownloadInfo> _recent = [];

    protected override void OnInitialized()
    {
        if (DownloadsService is null) return;

        DownloadsService.StateChanged += OnStateChanged;
        Refresh();
    }

    private void Refresh()
    {
        var snapshot = DownloadsService?.Snapshot() ?? [];
        _active = [.. snapshot.Where(d => d.State == DownloadState.Downloading)];
        _recent = [.. snapshot.Where(d => d.State != DownloadState.Downloading)];
    }

    private async Task CancelAsync(Guid mediaId)
    {
        if (DownloadsService is null) return;

        await DownloadsService.CancelAsync(mediaId);
        await DequeueQueuedPerformancesForAsync([mediaId]);
    }

    private async Task CancelAllAsync()
    {
        if (DownloadsService is null) return;

        var activeMediaIds = _active.Select(d => d.MediaId).ToHashSet();

        DownloadsService.CancelAll();
        await DequeueQueuedPerformancesForAsync(activeMediaIds);
    }

    // A media row can be enqueued while still Downloading (nothing gates EnqueueAsync on media
    // status), so cancelling here must also clear any queue row waiting on it. DeleteAsync would
    // try to cancel the download itself too, but DownloadsService.CancelAsync/CancelAll above
    // already settled it, so that second attempt is a no-op — DownloadsService has no reference
    // back to IPerformanceService, so neither path can re-trigger the other.
    private async Task DequeueQueuedPerformancesForAsync(ICollection<Guid> mediaIds)
    {
        if (PerformanceService is null || mediaIds.Count == 0) return;

        var queued = await PerformanceService.ReadQueuedAsync();
        foreach (var performance in queued.Where(p => mediaIds.Contains(p.MediaId)))
            await PerformanceService.DeleteAsync(performance.Id);
    }

    private static int Percent(double fraction) => (int)Math.Round(fraction * 100);

    private static string StateBadgeClass(DownloadState state) => state switch
    {
        DownloadState.Completed => "kh-badge--success",
        DownloadState.Failed => "kh-badge--danger",
        DownloadState.Cancelled => "kh-badge--secondary",
        _ => "kh-badge--secondary",
    };

    private async void OnStateChanged(object? sender, EventArgs e)
    {
        Refresh();
        await InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        if (DownloadsService is not null)
            DownloadsService.StateChanged -= OnStateChanged;
    }
}
