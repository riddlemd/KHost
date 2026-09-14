using KHost.Abstractions.Models;
namespace KHost.Abstractions.Services;

public enum ImportState { Idle, Running, Cancelling }

public interface IMediaImportService
{
    ImportState State { get; }
    int TotalCount { get; }
    int ImportedCount { get; }
    int FailedCount { get; }
    string? CurrentFilePath { get; }
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// Whether a file with a picture track is imported as a karaoke song or as plain video. The
    /// two are the same formats, so nothing in the file settles it — a host importing a folder of
    /// ad clips turns this off, and every other folder leaves it alone.
    /// </summary>
    bool VideoIsKaraoke { get; set; }

    /// <summary>
    /// What a particular file is, where the host has said so file by file rather than for the
    /// batch. Keyed by full path, and consulted before anything is worked out from the name — a
    /// folder holding both karaoke videos and ad clips cannot be settled by one answer, and this
    /// is how a host settles it row by row.
    /// </summary>
    /// <remarks>Empty for the ordinary import, where <see cref="VideoIsKaraoke"/> answers for all of them.</remarks>
    IDictionary<string, MediaType> TypeOverrides { get; }

    Task StartAsync(IEnumerable<string> filePaths);
    void Cancel();
}
