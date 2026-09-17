using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Keeps a file's download entry and library row moving together under one owner.</summary>
public interface IMediaAcquisitionService
{
    /// <summary>Where downloads and imports should live, always non-empty; may not exist yet.</summary>
    string MediaDirectory { get; }

    /// <summary>Registers the file as library media; idempotent by FilePath.</summary>
    Task<Guid> ImportAsync(MediaImportRequest request);

    /// <summary>Starts media as Downloading before it arrives; settle via Complete/Fail/Discard.</summary>
    Task<ImportTicket> BeginImportAsync(MediaImportRequest request);

    /// <summary>Progress as a fraction, clamped to [0,1]; unknown or settled ids no-op.</summary>
    Task ReportDownloadProgressAsync(Guid mediaId, double fraction);

    /// <summary>Progress as bytes; null <paramref name="totalBytes"/> means unknown size.</summary>
    Task ReportDownloadProgressAsync(Guid mediaId, long bytesReceived, long? totalBytes);

    /// <summary>Phase two of a download; optional. Only a Downloading row moves, not a settle.</summary>
    Task BeginProcessingAsync(Guid mediaId);

    /// <summary>Marks a row Ready: the download finished and the file is playable.</summary>
    Task CompleteImportAsync(Guid mediaId);

    /// <summary>Marks a row Broken; <paramref name="reason"/> shows beside it, never a stack trace.</summary>
    Task FailImportAsync(Guid mediaId, string? reason = null);

    /// <summary>Removes a cancelled row, checking FilePath: deletes if empty, else marks Broken.</summary>
    Task DiscardImportAsync(Guid mediaId);

}
