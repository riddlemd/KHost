using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Hands over the words and their timing for a file only this provider can read.</summary>
/// <remarks>The pair to <see cref="IMediaProbe"/>, and asked the same way: the provider claims the
/// file from its path, then answers from the file. A screen draws what comes back, so the host
/// never learns the container — the same reason a plugin describes a table instead of shipping
/// markup.
///
/// Separate from <see cref="IMediaProbe"/> because the questions have different costs and different
/// askers. A probe runs on every import and every reconcile and must stay cheap; this is read once
/// when a song loads, and a timing document is large enough that folding it into a probe result
/// would make every enqueue pay for it.</remarks>
public interface ITimedLyricsProvider
{
    /// <summary>Whether this provider has the timing for that file. Answered from the path alone.
    /// </summary>
    /// <remarks>Same rule as <see cref="IMediaProbe.CanProbe"/>: it is asked for files this
    /// provider does not own, and opening one to decide would cost a read per song.</remarks>
    bool CanProvide(string filePath);

    /// <summary>The words and their timing, or null when there are none to give.</summary>
    /// <remarks>Null is the ordinary answer for a file that simply has no timing, not an error. A
    /// song with none plays as it always has, with nothing drawn over it.</remarks>
    Task<TimedLyrics?> GetTimedLyricsAsync(string filePath, CancellationToken cancellationToken = default);
}
