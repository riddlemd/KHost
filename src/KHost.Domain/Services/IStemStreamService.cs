using KHost.Abstractions.Models;

namespace KHost.Domain.Services;

/// <summary>Encodes one stream from a song that a renderer answered with separate stems.</summary>
/// <remarks>Host-only, and so not in Abstractions: a renderer answers with stems and the host decides
/// whether the display can take them as they are. Implemented by the stream service, so the result is
/// one of its sessions and a key, tempo or mix change rebuilds it at the playhead like any other.</remarks>
public interface IStemStreamService
{
    /// <summary>Mixes <paramref name="stems"/> at their own levels into one stream, keyed and retimed,
    /// with <paramref name="words"/> burned in when there are any.</summary>
    /// <param name="sourcePath">The library file the stems came from; named in logs only.</param>
    /// <param name="backgroundPath">A clip to play under burned-in words; null, or a path that is not
    /// there, puts them over black.</param>
    /// <param name="adopt">The session the stems were written into. Closed with the new one, and also
    /// if this throws, since the caller is then left holding nothing to close it by.</param>
    Task<MediaStreamSession> OpenStemsAsync(
        string sourcePath,
        IReadOnlyList<StemSource> stems,
        TimeSpan startOffset,
        int pitch,
        int tempo,
        TimedLyrics? words,
        string? backgroundPath,
        MediaStreamSession? adopt,
        CancellationToken cancellationToken = default);
}
