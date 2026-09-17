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

    /// <summary>Whether a picture-track file imports as karaoke or video; nothing in it says.</summary>
    bool VideoIsKaraoke { get; set; }

    /// <summary>Per-path type answers, checked before the name, for a folder not all one thing.</summary>
    /// <remarks>Empty ordinarily, where <see cref="VideoIsKaraoke"/> answers for all of them.</remarks>
    IDictionary<string, MediaType> TypeOverrides { get; }

    Task StartAsync(IEnumerable<string> filePaths);
    void Cancel();
}
