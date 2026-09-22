using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Asks whichever provider has the timing for this file, so no caller has to know which.
/// </summary>
public interface ITimedLyricsService
{
    /// <summary>The words and their timing, or null when no provider has any for this file.</summary>
    /// <remarks>Null is the common answer, not a failure: nothing supplies timing for an mp4 or a
    /// CDG, and those play with the picture the host already streams. There is no fallback to ask,
    /// which is why this router has none where <see cref="IMediaProbeService"/> keeps ffprobe.
    /// </remarks>
    Task<TimedLyrics?> GetTimedLyricsAsync(string filePath, CancellationToken cancellationToken = default);
}
