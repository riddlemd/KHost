using KHost.Abstractions.Models;

namespace KHost.Domain.Services;

/// <summary>Encodes one stream from a song that a renderer answered with separate stems.</summary>
/// <remarks>Host-only, and so not in Abstractions: a renderer answers with stems and the host decides
/// whether the display can take them as they are. Implemented by the stream service, so the result is
/// one of its sessions and a key, tempo or mix change rebuilds it at the playhead like any other.</remarks>
public interface IStemStreamService
{
    /// <summary>Mixes <paramref name="stems"/> at their own levels into one stream, keyed and retimed,
    /// with <paramref name="words"/> burned in over black when there are any.</summary>
    /// <param name="sourcePath">The library file the stems came from; named in logs only.</param>
    /// <param name="adopt">The session the stems were written into. Closed with the new one, and also
    /// if this throws, since the caller is then left holding nothing to close it by.</param>
    Task<MediaStreamSession> OpenStemsAsync(
        string sourcePath,
        IReadOnlyList<StemSource> stems,
        TimeSpan startOffset,
        int pitch,
        int tempo,
        TimedLyrics? words,
        MediaStreamSession? adopt,
        CancellationToken cancellationToken = default);

    /// <summary>A stem's URL as ffmpeg should open it: the file on disk when it sits in one of this
    /// service's sessions, else the http address. Throws for anything else.</summary>
    string ResolveStemInput(string url);

    /// <summary>The stems mixed into the open stream at <paramref name="streamUrl"/>, at the levels
    /// they were mixed at; null for a stream not mixed from stems, or one no longer open.</summary>
    /// <remarks>As the renderer supplied them: unkeyed and at recorded speed, so they run in song
    /// time whatever the stream was retimed to.</remarks>
    IReadOnlyList<StemSource>? StemsMixedInto(string streamUrl);
}
