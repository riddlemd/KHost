using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Turns one file into something a display can actually play.</summary>
/// <remarks>Asked once, when a song starts, and it answers with what to play: a stream the host
/// encodes, a file the display reads directly, or the separate parts a display mixes for itself.
/// It does not decide what the show is, or where it comes out — it is told. See
/// <c>docs/media-renderer.md</c> for the shape's reasoning.
///
/// <para>This is not the render path that was removed. <c>IMediaPreparer</c> produced a cached
/// artifact ahead of time and grew eviction, progress reporting and state that outlived the song;
/// this is a per-play router that produces nothing the session does not sweep, and caches
/// nothing.</para></remarks>
public interface IMediaRenderer
{
    /// <summary>Whether this renderer owns that file.</summary>
    /// <remarks>Unlike <see cref="IMediaPlaybackGate.Claims"/> and <see cref="IMediaProbe.CanProbe"/>,
    /// which answer from the path alone because they run for every queued turn on every reconcile,
    /// this is asked <b>once per song, at play</b>. So it may open the file — which is what lets a
    /// renderer decide on what is inside a container rather than on its name.</remarks>
    bool CanRender(string filePath);

    /// <summary>What to play, or null to leave it to the next renderer.</summary>
    /// <remarks>Null is not a failure — it means this renderer had nothing better to offer for that
    /// target, and the fallback should encode as it always has. A renderer that cannot produce a
    /// file it claimed should throw, so the song fails with a cause rather than silently.</remarks>
    Task<MediaRendition?> RenderAsync(
        MediaRenderRequest request,
        CancellationToken cancellationToken = default);
}
