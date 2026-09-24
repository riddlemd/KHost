using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Tracks every download, active and settled: the cancel and registration surface.</summary>
/// <remarks>What the host's Downloads page shows. A plugin that downloads media should go through
/// <see cref="IMediaAcquisitionService"/> rather than this: that service moves the library row and
/// this entry together, and registering here directly leaves an entry no library row follows. A host
/// singleton, callable from any thread. Announces
/// <see cref="KHost.Abstractions.Messaging.Messages.DownloadsChanged"/> on every change that alters
/// what the page shows, and on progress only when the whole percentage moves.
///
/// <para>Entries are keyed by media id; an id that is unknown or already settled is ignored by
/// every reporting member rather than an error.</para></remarks>
public interface IDownloadsService
{
    /// <summary>Every Downloading entry, newest first, then the most recently settled (capped).</summary>
    /// <remarks>A copy; later changes do not reach it. Settled entries last only for this
    /// process.</remarks>
    IReadOnlyList<DownloadInfo> Snapshot();

    /// <summary>Cancels the download for this media id, marking it Cancelled. No-op if idle.</summary>
    /// <remarks>Fires the token handed out for it; the downloader is expected to stop and settle the
    /// library row. Leaves the row itself alone.</remarks>
    Task CancelAsync(Guid mediaId);

    /// <summary>Cancels every in-flight download at once, so none outlives the host on shutdown.</summary>
    void CancelAll();


    /// <summary>Registers a new Downloading entry and returns the token that fires on cancel.</summary>
    /// <remarks>Replaces any entry already registered for <paramref name="mediaId"/> without
    /// cancelling it; use <see cref="TokenForInFlight"/> when one may exist.</remarks>
    /// <param name="mediaId">The library row being downloaded; the entry's key.</param>
    /// <param name="title">Shown as the entry's title.</param>
    /// <param name="artist">Shown beneath the title.</param>
    /// <param name="source">The provider's name, shown beside the entry.</param>
    CancellationToken Register(Guid mediaId, string title, string artist, string source);

    /// <summary>Reuses the token for an id still Downloading, else registers a fresh one.</summary>
    CancellationToken TokenForInFlight(Guid mediaId, string title, string artist, string source);

    /// <summary>Moves an entry to a terminal state; <paramref name="reason"/> is shown beside it.</summary>
    /// <remarks>Passing <see cref="DownloadState.Downloading"/> does nothing. Write the reason for a
    /// host to act on, never a stack trace.</remarks>
    void Settle(Guid mediaId, DownloadState state, string? reason = null);

    /// <summary>Says which half is running, so the page reads a render apart from a download.</summary>
    /// <remarks>Changing phase clears the progress fraction, since each phase measures its own
    /// work.</remarks>
    void ReportPhase(Guid mediaId, DownloadPhase phase);

    /// <summary>Progress as bytes; <paramref name="totalBytes"/> null means unknown size.</summary>
    /// <remarks>A total of zero or less is treated as unknown, and the page then shows the bytes
    /// with no bar.</remarks>
    void ReportProgress(Guid mediaId, long bytesReceived, long? totalBytes);

    /// <summary>Progress as a fraction, clamped to [0,1]; unknown/settled ids no-op.</summary>
    /// <remarks>Cheap to call often.</remarks>
    void ReportProgress(Guid mediaId, double fraction);
}
