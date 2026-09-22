namespace KHost.Abstractions.Services;

/// <summary>Turns a file the host cannot open into one it can, for the moment it is played.</summary>
/// <remarks>For a provider whose own container ffmpeg does not read. The library row keeps the
/// original — that is the copy worth keeping, because it is the one this provider can always
/// rebuild from — and this hands over something playable only while a song is on.
///
/// This is deliberately not the render path that was removed. That one produced an artifact worth
/// minutes of CPU and megabytes on disk, so it grew a cache, an eviction rule, progress reporting
/// and state that outlived the song. **A resolver is a cheap transform and nothing else**: a
/// remux, a stream copy, an extraction. If one costs more than about a second, the work belongs at
/// import instead, and putting it here will make every song start wait for it.
///
/// The result is scratch. It is written into the working directory the host supplies and deleted
/// with it when the stream session closes, so a resolver keeps no files of its own and cleans up
/// nothing.</remarks>
public interface IPlayableMediaSource
{
    /// <summary>Whether this source has something to do with that file. Answered from the path
    /// alone, like <see cref="IMediaProbe.CanProbe"/>: it is asked before every stream opens,
    /// including for files nothing owns.</summary>
    bool CanResolve(string filePath);

    /// <summary>The path the host should open instead, or null to open the original.</summary>
    /// <param name="workingDirectory">Somewhere to write, already created. It is the stream
    /// session's own directory and is removed when the session closes.</param>
    /// <remarks>Null is not a failure — it means there was nothing to do. A resolver that cannot
    /// convert a file it claimed should throw, so the song fails loudly rather than handing ffmpeg
    /// a container it will reject with "Invalid data found".</remarks>
    Task<string?> ResolvePlayableAsync(
        string filePath,
        string workingDirectory,
        CancellationToken cancellationToken = default);
}
