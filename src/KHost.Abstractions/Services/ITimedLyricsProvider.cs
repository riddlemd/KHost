using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Hands over the words and their timing for a file only this provider can read.</summary>
/// <remarks>The pair to <see cref="IMediaProbe"/>, and asked the same way: the provider claims the
/// file from its path, then answers from the file. A display draws what comes back, so the host
/// never learns the container — the same reason a plugin describes a table instead of shipping
/// markup.
///
/// Separate from <see cref="IMediaProbe"/> because the questions have different costs and different
/// askers. A probe runs on every import and every reconcile and must stay cheap; this is read once
/// when a song loads, and a timing document is large enough that folding it into a probe result
/// would make every enqueue pay for it.
///
/// <para>An extension point: a plugin IMPLEMENTS it and the host discovers it. Providers are asked
/// in turn, and the first whose <see cref="CanProvide"/> is true answers; no other is asked after
/// it, even when it answers null. The plugin's object is one singleton shared across every extension
/// interface it implements, and is called from any thread.</para></remarks>
public interface ITimedLyricsProvider
{
    /// <summary>Whether this provider has the timing for that file. Answered from the path alone.
    /// </summary>
    /// <remarks>Same rule as <see cref="IMediaProbe.CanProbe"/>: it is asked for files this
    /// provider does not own, and opening one to decide would cost a read per song. A throw is
    /// logged and the provider skipped for that file.</remarks>
    bool CanProvide(string filePath);

    /// <summary>The words and their timing, or null when there are none to give.</summary>
    /// <remarks>Null is the ordinary answer for a file that simply has no timing, not an error. A
    /// song with none plays with nothing drawn over it. A throw is logged and treated as
    /// null.</remarks>
    Task<TimedLyrics?> GetTimedLyricsAsync(string filePath, CancellationToken cancellationToken = default);
}
