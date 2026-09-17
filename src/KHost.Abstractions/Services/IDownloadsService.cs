using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Tracks every download, active and settled: the cancel and registration surface.</summary>
public interface IDownloadsService
{
    /// <summary>Every Downloading entry, newest first, then the most recently settled (capped).</summary>
    IReadOnlyList<DownloadInfo> Snapshot();

    /// <summary>Cancels the download for this media id, marking it Cancelled. No-op if idle.</summary>
    Task CancelAsync(Guid mediaId);

    /// <summary>Cancels every in-flight download at once, so none outlives the host on shutdown.</summary>
    void CancelAll();


    /// <summary>Registers a new Downloading entry and returns the token that fires on cancel.</summary>
    CancellationToken Register(Guid mediaId, string title, string artist, string source);

    /// <summary>Reuses the token for an id still Downloading, else registers a fresh one.</summary>
    CancellationToken TokenForInFlight(Guid mediaId, string title, string artist, string source);

    /// <summary>Moves an entry to a terminal state; <paramref name="reason"/> is shown beside it.</summary>
    void Settle(Guid mediaId, DownloadState state, string? reason = null);

    /// <summary>Says which half is running, so the page reads a render apart from a download.</summary>
    void ReportPhase(Guid mediaId, DownloadPhase phase);

    /// <summary>Progress as bytes; <paramref name="totalBytes"/> null means unknown size.</summary>
    void ReportProgress(Guid mediaId, long bytesReceived, long? totalBytes);

    /// <summary>Progress as a fraction, clamped to [0,1]; unknown/settled ids no-op.</summary>
    void ReportProgress(Guid mediaId, double fraction);
}
