using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>
/// Bringing a file into the library, and keeping its download entry and its row in step. One owner
/// on purpose: a caller settling the download without moving the media status, or the reverse,
/// leaves the Downloads page and the library disagreeing about the same file.
///
/// Resolved from DI like any other service. It was reached through IPluginContext.Library when a
/// plugin could reach nothing else; a plugin now injects this beside IMediaService or
/// IPerformanceService as it needs them.
/// </summary>
public interface IMediaAcquisitionService
{
    /// <summary>
    /// The directory downloads and imports should live under. Always non-empty; may not exist
    /// yet — callers create what they need.
    /// </summary>
    string MediaDirectory { get; }

    /// <summary>
    /// Registers the file as library media and returns its id. Idempotent by
    /// <see cref="MediaImportRequest.FilePath"/> — importing the same path again returns the
    /// existing row's id rather than creating a duplicate, so a re-download is safe to retry.
    /// </summary>
    Task<Guid> ImportAsync(MediaImportRequest request);

    /// <summary>
    /// Registers the file as library media before it has actually been retrieved, so the console
    /// can show download progress on a slow connection — the row is created in the Downloading
    /// state rather than Ready. Idempotent the same way as <see cref="ImportAsync"/>: importing
    /// the same <see cref="MediaImportRequest.FilePath"/> again returns the existing row's id
    /// as-is, whatever its current status — an already-Ready row is never regressed back to
    /// Downloading. Resolve the row with <see cref="CompleteImportAsync"/>,
    /// <see cref="FailImportAsync"/>, or <see cref="DiscardImportAsync"/>; those are the only ways
    /// a plugin may settle it.
    /// </summary>
    Task<ImportTicket> BeginImportAsync(MediaImportRequest request);

    /// <summary>
    /// Reports progress on a download begun with <see cref="BeginImportAsync"/>, shown on the
    /// host's Downloads page. Fraction is clamped to [0,1]; an unknown or already-settled media id
    /// is a silent no-op. Callers may report at any cadence — the host throttles its own updates.
    /// </summary>
    Task ReportDownloadProgressAsync(Guid mediaId, double fraction);

    /// <summary>
    /// The same report as a byte count, when the provider knows one — the page then shows how much
    /// of how much rather than a bare percentage. <paramref name="totalBytes"/> null means the size
    /// is unknown, and the bar stays indeterminate while the count still moves.
    /// </summary>
    Task ReportDownloadProgressAsync(Guid mediaId, long bytesReceived, long? totalBytes);

    /// <summary>
    /// Phase two of a download begun with <see cref="BeginImportAsync"/>: the bytes are in, and
    /// the file is being turned into something playable. Not a settle — the row is still in
    /// flight, its download entry still reads Downloading, and one of
    /// <see cref="CompleteImportAsync"/>, <see cref="FailImportAsync"/> or
    /// <see cref="DiscardImportAsync"/> still has to resolve it. Only a Downloading row moves; an
    /// unknown or already-settled media id is a silent no-op, so a late call cannot drag a
    /// finished row back into flight. Optional: a plugin that goes straight from bytes to a
    /// playable file never calls it and stays Downloading throughout.
    /// </summary>
    Task BeginProcessingAsync(Guid mediaId);

    /// <summary>
    /// Marks a row begun with <see cref="BeginImportAsync"/> Ready — the download finished and the
    /// file is playable.
    /// </summary>
    Task CompleteImportAsync(Guid mediaId);

    /// <summary>
    /// Marks a row begun with <see cref="BeginImportAsync"/> Broken — the download failed.
    /// <paramref name="reason"/> is shown beside the failure on the Downloads page: say what a host
    /// could act on — "checksum did not match", "ffmpeg exited 1" — never a stack trace, since it
    /// is read at a glance in the middle of a show.
    /// </summary>
    Task FailImportAsync(Guid mediaId, string? reason = null);

    /// <summary>
    /// Removes a row begun with <see cref="BeginImportAsync"/> whose download was cancelled, in
    /// either phase — delete any partial file first. The host checks
    /// <see cref="MediaImportRequest.FilePath"/> itself rather than taking the caller's word: with
    /// nothing there the row goes, and a row whose file outlived the cancel is kept as Broken
    /// instead. So a file can never be left on disk with no row pointing at it, whatever the
    /// caller believes it cleaned up, and a delete that quietly failed shows up as a Broken row
    /// rather than as a file the folder scan re-imports later as Ready.
    ///
    /// A settled row (Ready or Broken) is left alone entirely.
    /// </summary>
    Task DiscardImportAsync(Guid mediaId);

}
