using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.BurnIn;

namespace KHost.Domain.Services;

/// <summary>Encodes whatever nothing else claimed, which is what makes rendering total.</summary>
/// <remarks>The renderer of last resort, and the only one registered keyed: it claims every file,
/// so it must never enter the race with the renderers that claim one.
///
/// <para>A face over <see cref="IMediaStreamService"/> rather than a replacement for it. The
/// ffmpeg argument building was never the problem — being the only answer was.</para>
///
/// <para>Honours <see cref="RenderTarget.BurnLyrics"/> for any song with timed words, whoever
/// supplied them: the words are painted into the same encode, so key, tempo and the mix still
/// apply. A song with none gets the ordinary encode.</para></remarks>
/// <remarks>Open rather than sealed so a format with rules of its own can inherit the encode while
/// owning its own claim — see <c>CompactDiscPlusGraphicsRenderer</c>. A subclass that later grows a way to
/// play its format without ffmpeg replaces the body and nothing above it changes.</remarks>
/// <param name="burnIn">Null for a renderer whose format carries its words in its own picture.</param>
public class StreamingMediaRenderer(IMediaStreamService streams, LyricBurnIn? burnIn = null) : IMediaRenderer
{
    /// <summary>The stream service the encode runs through, for whoever inherits this.</summary>
    protected IMediaStreamService Streams { get; } = streams;

    /// <summary>Everything. A file nothing understands is still a file ffmpeg will try.</summary>
    public virtual bool CanRender(string filePath) => true;

    public virtual async Task<MediaRendition?> RenderAsync(
        MediaRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = request.Target.BurnLyrics && burnIn is not null
            ? await burnIn.OpenAsync(request, cancellationToken)
            : null;

        session ??= await Streams.OpenAsync(
            request.FilePath,
            request.StartOffset,
            request.Pitch,
            request.Tempo,
            request.Mix,
            cancellationToken);

        return new MediaRendition
        {
            Url = session.PlaylistUrl,
            StartOffset = session.StartOffset,
            Tempo = session.Tempo,
            Pitch = session.Pitch,

            // Cut at the playhead, so its zero is the offset above and a seek past what ffmpeg has
            // written lands on nothing. Only a whole-song rendition is free to seek.
            SeekableInPlace = false,

            Session = session,
        };
    }
}
