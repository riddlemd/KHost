using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Turns one file into something a display can actually play.</summary>
/// <remarks>Asked once, when a song starts, and it answers with what to play: a stream the host
/// encodes, a file the display reads directly, or the separate parts a display mixes for itself.
/// It does not decide what the show is, or where it comes out — it is told. See
/// <c>docs/media-renderer.md</c> for the shape's reasoning.
///
/// <para>A per-play router, not a preparation step: it produces nothing that outlives the song,
/// caches nothing and reports no progress. Files it writes belong in a session from
/// <see cref="IMediaStreamService.OpenWithoutEncodeAsync"/>, handed back as
/// <see cref="MediaRendition.Session"/>; whoever holds the rendition closes it, so a renderer
/// cleans up nothing itself.</para>
///
/// <para>An extension point: a plugin IMPLEMENTS it and the host discovers it. The host asks the
/// renderers in turn and the first whose <see cref="CanRender"/> is true answers; when no renderer
/// claims the file, or the claiming one returns null, the host's own encode plays it. A renderer may
/// also burn things into the picture for a display that cannot draw them, such as a song's words.
/// The plugin's object is one singleton shared across every extension interface it implements, and
/// is called from any thread.</para></remarks>
public interface IMediaRenderer
{
    /// <summary>Whether this renderer owns that file.</summary>
    /// <remarks>Unlike <see cref="IMediaPlaybackGate.Claims"/> and <see cref="IMediaProbe.CanProbe"/>,
    /// which answer from the path alone because they run for every queued turn on every reconcile,
    /// this is asked <b>once per song, at play</b>. So it may open the file — which is what lets a
    /// renderer decide on what is inside a container rather than on its name. A throw here is logged
    /// and the renderer skipped for that file.</remarks>
    bool CanRender(string filePath);

    /// <summary>What to play, or null to leave it to the host's own encode.</summary>
    /// <remarks>Null is not a failure — it means this renderer had nothing better to offer for that
    /// target, and the host encodes the file as it would any other; no other renderer is asked. Read
    /// <see cref="MediaRenderRequest.Target"/>: stems are worth offering only to a target that mixes
    /// them.</remarks>
    /// <exception cref="KHost.Abstractions.Exceptions.KHostException">Throw this when a file this
    /// renderer claimed cannot be produced, with a line a host can act on; it reaches the host as
    /// written. Any other exception is wrapped in a generic one. Either way the song fails with a
    /// cause rather than silently.</exception>
    Task<MediaRendition?> RenderAsync(
        MediaRenderRequest request,
        CancellationToken cancellationToken = default);
}
