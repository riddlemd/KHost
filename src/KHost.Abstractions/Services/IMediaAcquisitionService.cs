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
    /// Marks a row begun with <see cref="BeginImportAsync"/> Ready — the download finished and the
    /// file is playable.
    /// </summary>
    Task CompleteImportAsync(Guid mediaId);

    /// <summary>Marks a row begun with <see cref="BeginImportAsync"/> Broken — the download failed.</summary>
    Task FailImportAsync(Guid mediaId);

    /// <summary>
    /// Removes a row begun with <see cref="BeginImportAsync"/> whose download was cancelled before
    /// any file survived. Call this ONLY after verifying no destination file remains on disk — a
    /// row is deleted here only while it is still Downloading (never once it is Ready or Broken),
    /// so that a file left behind by a cancel can never be silently unregistered. If any file
    /// remains, call <see cref="FailImportAsync"/> instead so the row stays Broken.
    /// </summary>
    Task DiscardImportAsync(Guid mediaId);

}
