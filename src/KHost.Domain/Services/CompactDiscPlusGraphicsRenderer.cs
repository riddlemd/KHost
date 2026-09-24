using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common.Media;

namespace KHost.Domain.Services;

/// <summary>Renders a CDG, and owns what a CDG needs before it is worth rendering at all.</summary>
/// <remarks>A <c>.cdg</c> is half a song. The graphics carry the words and nothing else — the sound
/// lives in an <c>.mp3</c> of the same name beside it, and the pair is the media. One without the
/// other is not a quiet song but an incomplete one, and it used to reach the room as a silent
/// stream with a warning in a log nobody was reading.
///
/// <para>The encode is inherited because subcode graphics have to be decoded into a picture and
/// ffmpeg is what does that today. It is its own renderer anyway, so the rules that belong to the
/// format have somewhere to live — and so the day a CDG is drawn natively on the screen, the way a
/// kit's stems are now mixed there, this body is what changes and nothing above it notices.</para>
/// </remarks>
public sealed class CompactDiscPlusGraphicsRenderer(IMediaStreamService streams) : StreamingMediaRenderer(streams)
{
    public override bool CanRender(string filePath)
        => Path.GetExtension(filePath).Equals(MediaFormats.KaraokeGraphicsExtension, StringComparison.OrdinalIgnoreCase);

    public override Task<MediaRendition?> RenderAsync(
        MediaRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (MediaFormats.FindKaraokeAudio(request.FilePath) is null)
        {
            // Named, not merely refused: the host can put the file back, and "it played silently"
            // is the one symptom that never points at its own cause.
            throw new KHostException(
                $"“{Path.GetFileName(request.FilePath)}” has no audio beside it, so there is nothing to play.",
                $"A CDG needs “{Path.GetFileNameWithoutExtension(request.FilePath)}.mp3” beside it. Put it back, then try again.",
                "KH-CDG-NO-AUDIO");
        }

        return base.RenderAsync(request, cancellationToken);
    }
}
