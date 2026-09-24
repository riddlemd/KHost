using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class DownloadsManagerPage : IDisposable
{
    [Inject] private IDownloadsService? DownloadsService { get; set; }
    [Inject] private IPerformanceService? PerformanceService { get; set; }
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private List<DownloadInfo> _active = [];
    private List<DownloadInfo> _recent = [];

    protected override void OnInitialized()
    {
        _subscriptions.Add(Broker.Subscribe<DownloadsChanged>(OnStateChanged));
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

    // A media row can be enqueued while still Downloading, so cancelling here must also clear any
    // queue row waiting on it. DeleteAsync's own cancel then no-ops, already settled above.
    private async Task DequeueQueuedPerformancesForAsync(ICollection<Guid> mediaIds)
    {
        if (PerformanceService is null || mediaIds.Count == 0) return;

        var queued = await PerformanceService.ReadQueuedAsync();
        foreach (var performance in queued.Where(p => mediaIds.Contains(p.MediaId)))
            await PerformanceService.DeleteAsync(performance.Id);
    }

    private static int Percent(double fraction) => (int)Math.Round(fraction * 100);

    /// <summary>What the bar beside it is measuring: the two halves of one acquisition.</summary>
    private static string PhaseLabel(DownloadPhase phase) => phase switch
    {
        DownloadPhase.Processing => "Processing",
        _ => "Fetching",
    };

    /// <summary>Null when the provider counts no bytes, so the phase word stands alone.</summary>
    private static string? Sizes(DownloadInfo download) => download switch
    {
        { BytesReceived: null } => null,
        { BytesReceived: { } received, TotalBytes: { } total } => $"{Megabytes(received)} of {Megabytes(total)}",
        { BytesReceived: { } received } => Megabytes(received),
    };

    /// <summary>Only while bytes arrive, since a still count beside a moving bar reads as a stall.</summary>
    private static string? SizesWhileFetching(DownloadInfo download)
        => download.Phase == DownloadPhase.Fetching ? Sizes(download) : null;

    private static string Megabytes(long bytes) => $"{bytes / 1024d / 1024d:0.#} MB";

    private static string Elapsed(DownloadInfo download)
    {
        var span = (download.SettledUtc ?? DateTime.UtcNow) - download.StartedUtc;

        // Clamped: a row whose clock is a moment ahead of this render would otherwise count down.
        return span < TimeSpan.Zero ? "0:00" : $"{(int)span.TotalMinutes}:{span.Seconds:00}";
    }

    /// <summary>The one line about a settled row: a failure says why, success says what it cost.</summary>
    private static string Detail(DownloadInfo download)
    {
        if (!string.IsNullOrWhiteSpace(download.Reason))
            return download.Reason;

        if (download.State != DownloadState.Completed)
            return string.Empty;

        var size = Sizes(download with { TotalBytes = null });

        return size is null ? Elapsed(download) : $"{Elapsed(download)} · {size}";
    }

    private static string StateBadgeClass(DownloadState state) => state switch
    {
        DownloadState.Completed => "kh-badge--success",
        DownloadState.Failed => "kh-badge--danger",
        _ => "kh-badge--secondary",
    };

    private void OnStateChanged(DownloadsChanged message)
        => _ = InvokeAsync(() =>
        {
            Refresh();
            StateHasChanged();
        });

    public void Dispose() => _subscriptions.Dispose();
}
