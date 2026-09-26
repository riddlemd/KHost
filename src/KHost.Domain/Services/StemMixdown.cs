using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.BurnIn;

namespace KHost.Domain.Services;

/// <summary>Turns a renderer's stems into one stream, for a target that cannot take them as they are.
/// </summary>
/// <remarks>Host-only. A renderer answers with what the song is made of; what a display needs from
/// that is the host's decision, so a plugin that supplies stems gets a receiver, a key change and
/// burned-in words without doing any of it itself.</remarks>
public interface IStemMixdown
{
    /// <summary>One stream from the rendition's stems, cut at the request's playhead with its key,
    /// tempo and burned-in words. Takes ownership of the rendition's session.</summary>
    Task<MediaRendition> EncodeAsync(
        MediaRendition stems,
        MediaRenderRequest request,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class StemMixdown(IStemStreamService streams, IMediaStreamService sessions, LyricBurnIn burnIn) : IStemMixdown
{
    /// <summary>Whether a rendition of stems alone needs the host's encode before this target can
    /// play it.</summary>
    /// <remarks>Stems carry no key, speed or picture: a display that mixes them can take them only
    /// when none of those was asked for. A rendition that already carries a URL made its own
    /// arrangement for the targets that cannot mix.</remarks>
    public static bool IsNeeded(MediaRendition rendition, MediaRenderRequest request)
        => rendition.Url is not { Length: > 0 }
           && rendition.Stems.Count > 0
           && (!request.Target.MixesStems
               || request.Target.BurnLyrics
               || request.Pitch != 0
               || request.Tempo != 0);

    public async Task<MediaRendition> EncodeAsync(
        MediaRendition stems,
        MediaRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        (TimedLyrics Words, string? BackgroundPath)? found;

        try
        {
            found = request.Target.BurnLyrics ? await burnIn.FindAsync(request.FilePath, cancellationToken) : null;
        }
        catch when (stems.Session is not null)
        {
            // Nothing has adopted the renderer's session yet, and nothing else will close it.
            await sessions.CloseAsync(stems.Session.Id);
            throw;
        }

        var session = await streams.OpenStemsAsync(
            request.FilePath,
            stems.Stems,
            request.StartOffset,
            request.Pitch,
            request.Tempo,
            found?.Words,
            found?.BackgroundPath,
            stems.Session,
            cancellationToken);

        return new MediaRendition
        {
            Url = session.PlaylistUrl,
            StartOffset = session.StartOffset,
            Tempo = session.Tempo,
            Pitch = session.Pitch,

            // Cut at the playhead, like any host encode: a seek past what is written lands on nothing.
            SeekableInPlace = false,
            Session = session,
        };
    }
}
