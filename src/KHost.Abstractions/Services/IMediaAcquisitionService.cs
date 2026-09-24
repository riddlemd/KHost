using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Keeps a file's download entry and library row moving together under one owner.</summary>
/// <remarks>What a plugin TAKES in its constructor to put a downloaded file into the library. It
/// owns three rules nothing else may re-implement: an import is idempotent by file path, the row's
/// status and the <see cref="IDownloadsService"/> entry move together, and
/// <see cref="DiscardImportAsync"/> deletes only a row still being acquired. Enqueuing is not
/// here: compose <see cref="ISingerQueueService.SelectedUserId"/> with
/// <see cref="IPerformanceService.CreateAndEnqueueAsync"/>.
///
/// <para>A host singleton, callable from any thread. Announces nothing itself; the row changes
/// announce <see cref="KHost.Abstractions.Messaging.Messages.MediaLibraryChanged"/> and the entry
/// changes <see cref="KHost.Abstractions.Messaging.Messages.DownloadsChanged"/>.</para>
///
/// <para>The flow: <see cref="BeginImportAsync"/>, progress reports, optionally
/// <see cref="BeginProcessingAsync"/>, then exactly one settle — <see cref="CompleteImportAsync"/>,
/// <see cref="FailImportAsync"/> or <see cref="DiscardImportAsync"/>. A row left unsettled is marked
/// Broken on the host's next start.</para></remarks>
public interface IMediaAcquisitionService
{
    /// <summary>Where downloads and imports should live, always non-empty; may not exist yet.</summary>
    /// <remarks>The host's setting, re-read on every access so a change applies without a restart;
    /// create the folder before writing into it.</remarks>
    string MediaDirectory { get; }

    /// <summary>Registers the file as library media; idempotent by FilePath.</summary>
    /// <remarks>For a file already complete on disk: the row is made Ready at once, with no
    /// download entry.</remarks>
    /// <returns>The new row's id, or the id of the row already at that path, which is returned
    /// unchanged whatever its status.</returns>
    Task<Guid> ImportAsync(MediaImportRequest request);

    /// <summary>Starts media as Downloading before it arrives; settle via Complete/Fail/Discard.</summary>
    /// <returns>A ticket naming the row and a token that fires when the host cancels the download.
    /// When a row already has that path, no new row is made: its id comes back, with the in-flight
    /// download's token if it is still being acquired, or a token that never fires if it has
    /// already settled.</returns>
    Task<ImportTicket> BeginImportAsync(MediaImportRequest request);

    /// <summary>Progress as a fraction, clamped to [0,1]; unknown or settled ids no-op.</summary>
    Task ReportDownloadProgressAsync(Guid mediaId, double fraction);

    /// <summary>Progress as bytes; null <paramref name="totalBytes"/> means unknown size.</summary>
    Task ReportDownloadProgressAsync(Guid mediaId, long bytesReceived, long? totalBytes);

    /// <summary>Phase two of a download; optional. Only a Downloading row moves, not a settle.</summary>
    /// <remarks>For a provider that must turn the bytes into something playable once they are in and
    /// verified. Moves the row to Processing and the entry's phase with it, clearing its progress; the
    /// entry stays in flight. Any other row, or an unknown id, is left alone. Ask
    /// <c>MediaStatuses.IsAcquiring()</c> rather than comparing against Downloading, or a row in this
    /// phase is missed.</remarks>
    Task BeginProcessingAsync(Guid mediaId);

    /// <summary>Marks a row Ready: the download finished and the file is playable.</summary>
    /// <remarks>An unknown id is logged and ignored.</remarks>
    Task CompleteImportAsync(Guid mediaId);

    /// <summary>Marks a row Broken; <paramref name="reason"/> shows beside it, never a stack trace.</summary>
    /// <remarks>An unknown id is logged and ignored.</remarks>
    Task FailImportAsync(Guid mediaId, string? reason = null);

    /// <summary>Removes a cancelled row, checking FilePath: deletes if empty, else marks Broken.</summary>
    /// <remarks>Settles the entry as Cancelled. Only a row still being acquired, in either phase, is
    /// touched: it is deleted when nothing is at its path, and kept as Broken when a file outlived the
    /// cancel, so a partial is never left with no row pointing at it. Ready and Broken rows are left
    /// alone, and a second call is harmless.</remarks>
    Task DiscardImportAsync(Guid mediaId);

}
