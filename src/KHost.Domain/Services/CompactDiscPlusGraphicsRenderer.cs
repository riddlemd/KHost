using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common.Media;

namespace KHost.Domain.Services;

/// <summary>Renders a CDG, and owns what a CDG needs before it is worth rendering at all.</summary>
/// <remarks>A <c>.cdg</c> is half a song. The graphics carry the words and nothing else — the sound
/// lives in an audio file of the same name beside it, and the pair is the media. One without the
/// other is an incomplete song, not a quiet one, so it is refused rather than streamed silent.
///
/// <para>The encode is inherited because subcode graphics have to be decoded into a picture and
/// ffmpeg is what does that today. It is its own renderer anyway, so the rules that belong to the
/// format have somewhere to live — and so the day a CDG is drawn natively on the screen, the way a
/// stems format's parts are now mixed there, this body is what changes and nothing above it notices.</para>
/// </remarks>
public sealed class CompactDiscPlusGraphicsRenderer(IMediaStreamService streams) : StreamingMediaRenderer(streams)
{
    public override bool CanRender(string filePath) => MediaFormats.IsCompactDiscGraphics(filePath);

    public override Task<MediaRendition?> RenderAsync(
        MediaRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        // A zipped pair is judged whole here, so a bad zip fails with its own reason before an
        // encode is opened; the audio is found beside the .cdg once it is written out.
        if (MediaFormats.IsKaraokeArchive(request.FilePath))
        {
            KaraokeZip.Validate(request.FilePath);
            return base.RenderAsync(request, cancellationToken);
        }

        if (MediaFormats.FindKaraokeAudio(request.FilePath) is null)
        {
            // Named, not merely refused: the host can put the file back, and "it played silently"
            // is the one symptom that never points at its own cause.
            throw new KHostException(
                $"“{Path.GetFileName(request.FilePath)}” has no audio beside it, so there is nothing to play.",
                $"A CDG needs an audio file named “{Path.GetFileNameWithoutExtension(request.FilePath)}” beside it, such as an .mp3. Put it back, then try again.",
                "KH-CDG-NO-AUDIO");
        }

        return base.RenderAsync(request, cancellationToken);
    }
}
