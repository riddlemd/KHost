using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>
/// Tracks every plugin download the host knows about — active and recently settled — behind the
/// Downloads management page. Also the host-side entry point a queue dequeue uses to cancel a
/// download it owns, and the registration surface <c>PluginLibrary</c> uses to begin, report
/// progress on, and settle one.
/// </summary>
public interface IDownloadsService
{
    /// <summary>Every Downloading entry, newest first, followed by the most recently settled ones (capped).</summary>
    IReadOnlyList<DownloadInfo> Snapshot();

    /// <summary>Cancels the registered download for this media id and marks it Cancelled. No-op if none is in flight.</summary>
    Task CancelAsync(Guid mediaId);

    /// <summary>Cancels every in-flight download at once, so none outlives the host on shutdown.</summary>
    void CancelAll();

    event EventHandler StateChanged;

    /// <summary>Registers a new Downloading entry and returns the token that fires on cancel.</summary>
    CancellationToken Register(Guid mediaId, string title, string artist, string source);

    /// <summary>
    /// Reuses the token already registered for a media id still Downloading, or registers a fresh
    /// one from the given metadata if none is tracked (e.g. the row survived a restart, which
    /// clears this in-memory registry but not the database).
    /// </summary>
    CancellationToken TokenForInFlight(Guid mediaId, string title, string artist, string source);

    /// <summary>Moves an active entry to a terminal state and into the recent list. No-op for an id with no active entry.</summary>
    void Settle(Guid mediaId, DownloadState state);

    /// <summary>Records progress for an active download. Fraction is clamped to [0,1]; unknown/settled ids are a silent no-op.</summary>
    void ReportProgress(Guid mediaId, double fraction);
}
