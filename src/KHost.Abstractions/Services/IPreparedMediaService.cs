using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Renders queued songs ahead of time, so starting one is a copy rather than a transcode.</summary>
/// <remarks>Playback runs one ffmpeg per song and it transcodes flat out, so the cost lands on the
/// song transition, which is the worst moment a room can see. Rendering ahead moves that work to a
/// point with no deadline and leaves a stream copy behind.</remarks>
public interface IPreparedMediaService
{
    /// <summary>Brings the renders in line with the queue: what is queued gets one, what is not
    /// loses the one it had. Driven by the queue changing, so a song sung, removed or never played
    /// all release their space the same way.</summary>
    Task ReconcileAsync(CancellationToken cancellationToken = default);

    /// <summary>What a queued turn can do right now. Asked of the turn rather than the library row
    /// behind it: the row says what KHost has, this says whether this performance has something to
    /// start.</summary>
    PerformancePreparation StateFor(string filePath);

    /// <summary>Whether a plugin owns this format, so the file cannot be played as it is. Such a
    /// turn is unplayable until its render lands, where an ordinary file merely transcodes.</summary>
    bool RequiresPreparation(string filePath);

    /// <summary>The finished render for a file, or null when there is none to use yet. Never blocks
    /// on one in flight: a host who plays before it is ready gets today's transcode instead.</summary>
    string? TryResolve(string filePath);

    /// <summary>Drops every render. Nothing here outlives the process that made it, since the venue
    /// settings and the source files it was built against can both change while the host is down.
    /// </summary>
    void Sweep();
}
