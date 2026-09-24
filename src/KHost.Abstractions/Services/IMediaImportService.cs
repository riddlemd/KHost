using KHost.Abstractions.Models;
namespace KHost.Abstractions.Services;

/// <summary>Where a bulk import stands: not running, working through files, or winding down after a
/// cancel.</summary>
public enum ImportState
{
    /// <summary>No import is running; a new one may start.</summary>
    Idle,

    /// <summary>An import is working through its files.</summary>
    Running,

    /// <summary>A cancel was asked for and the run is winding down; it returns to
    /// <see cref="Idle"/> once it stops.</summary>
    Cancelling
}

/// <summary>Turns a batch of files a host picked into library rows, in the background.</summary>
/// <remarks>Host-owned: the importer behind the console's Import button. A plugin does not drive it;
/// a plugin adds file types to it with its manifest's <c>importFormats</c>, and a plugin that
/// downloads media registers each file through <see cref="IMediaAcquisitionService"/> instead. One
/// run at a time. A file already in the library, by path or by identical content, is skipped, and
/// the audio half of a karaoke pair becomes part of its graphics file's row rather than a row of its
/// own. A host singleton, callable from any thread. Announces
/// <see cref="KHost.Abstractions.Messaging.Messages.MediaImportChanged"/> as the run starts,
/// progresses (at most a few times a second), is cancelled and finishes; each row it creates
/// announces <see cref="KHost.Abstractions.Messaging.Messages.MediaLibraryChanged"/>.</remarks>
public interface IMediaImportService
{
    /// <summary>Where the current run stands.</summary>
    ImportState State { get; }

    /// <summary>Files the run will import, once those already in the library are set aside.</summary>
    int TotalCount { get; }

    /// <summary>Files made into rows so far in this run.</summary>
    int ImportedCount { get; }

    /// <summary>Files this run could not import, including a graphics-only karaoke file with no
    /// audio beside it.</summary>
    int FailedCount { get; }

    /// <summary>The file being imported now, or null between files and when idle.</summary>
    string? CurrentFilePath { get; }

    /// <summary>Every extension the importer accepts: the host's own plus those declared by loaded
    /// plugins, each lower case with a leading dot.</summary>
    /// <remarks>A filter, not a promise the host can play the file. Fixed for the life of the
    /// process, as plugins are.</remarks>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>Whether a picture-track file imports as karaoke or video; nothing in it says.</summary>
    bool VideoIsKaraoke { get; set; }

    /// <summary>Per-path type answers, checked before the name, for a folder not all one thing.</summary>
    /// <remarks>Empty ordinarily, where <see cref="VideoIsKaraoke"/> answers for all of them. Paths
    /// match without regard to case.</remarks>
    IDictionary<string, MediaType> TypeOverrides { get; }

    /// <summary>Starts importing <paramref name="filePaths"/> in the background and returns at once.
    /// </summary>
    /// <remarks>Ignored while a run is already going, or when nothing is left once paired audio is
    /// set aside. Watch <see cref="State"/> or the announcements for the outcome; a failure on one
    /// file is counted in <see cref="FailedCount"/>, never thrown.</remarks>
    Task StartAsync(IEnumerable<string> filePaths);

    /// <summary>Asks the running import to stop; rows already made stay. Does nothing when idle.
    /// </summary>
    void Cancel();
}
