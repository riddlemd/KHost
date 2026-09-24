using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Asks whichever provider has the timing for this file, so no caller has to know which.
/// </summary>
/// <remarks>The router over every <see cref="ITimedLyricsProvider"/>. A display provider whose
/// device draws words TAKES it and reads a song's timing while loading it. A plugin supplies timing
/// by implementing <see cref="ITimedLyricsProvider"/>, not this. A host singleton, callable from any
/// thread. Announces nothing.</remarks>
public interface ITimedLyricsService
{
    /// <summary>The words and their timing, or null when no provider has any for this file.</summary>
    /// <remarks>Null is the common answer, not a failure: nothing supplies timing for an mp4 or a
    /// CDG, and those play with the picture the host already streams. There is no fallback to ask,
    /// which is why this router has none where <see cref="IMediaProbeService"/> has the host's own
    /// reader. A provider that throws is logged and answers null.</remarks>
    /// <exception cref="OperationCanceledException">When <paramref name="cancellationToken"/>
    /// fires.</exception>
    Task<TimedLyrics?> GetTimedLyricsAsync(string filePath, CancellationToken cancellationToken = default);
}
