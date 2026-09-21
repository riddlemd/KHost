namespace KHost.Abstractions.Services;

/// <summary>Turns a file the host cannot play into one it can, when a turn needs it.</summary>
/// <remarks>For a format only the plugin understands. One may be stems and a timing
/// document, not a video, so nothing downstream can open it: the plugin renders it and the host
/// plays what comes out. The host asks for this because a turn was queued, never at import, so the
/// playable copy lives as long as the turn does rather than forever.</remarks>
public interface IMediaPreparer
{
    /// <summary>Whether this preparer owns the file. A file nobody claims is played as it is.</summary>
    /// <remarks>Asked of the path, so it must not read the file: this is called for every queued
    /// turn on every reconcile.</remarks>
    bool CanPrepare(string filePath);

    /// <summary>How far apart this preparer puts keyframes in what it renders, in seconds.</summary>
    /// <remarks>A muxer can only cut a copy where a keyframe already is, so the host carries a
    /// render's picture across untouched only when its own segment length is a multiple of this.
    /// Null says "I do not know", and the host re-encodes rather than guess: the failure is not a
    /// broken stream but segments quietly stretched to the next keyframe.
    /// <para>A default body, which is behaviour living in Abstractions and the same deliberate
    /// exception <see cref="IMediaPlaybackGate.Claims"/> is. It buys the same thing: a preparer
    /// written before this exists compiles and loads unchanged, so the contract did not break.
    /// </para></remarks>
    int? KeyframeSeconds => null;

    /// <summary>Renders it to <paramref name="destination"/>. False when it could not be made, which
    /// leaves the turn unplayable rather than playing something wrong.</summary>
    /// <remarks>Long: a render is minutes of CPU. The host calls this off the path a host is
    /// waiting on and holds it to one at a time.</remarks>
    Task<bool> PrepareAsync(string filePath, string destination, CancellationToken cancellationToken = default);
}
