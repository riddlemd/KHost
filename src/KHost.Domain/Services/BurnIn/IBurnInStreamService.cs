using KHost.Abstractions.Models;

namespace KHost.Domain.Services.BurnIn;

/// <summary>Opens a song's stream with its timed words burned into the picture.</summary>
/// <remarks>Host-only, and so not in Abstractions: a plugin reaches burned-in words by supplying
/// <see cref="TimedLyrics"/> and letting a display ask for them, never by opening one of these
/// itself. Implemented by the stream service, so the burn-in is the same encode with one more
/// input, and a key, tempo or mix change still rebuilds it at the playhead.</remarks>
public interface IBurnInStreamService
{
    /// <summary>As <see cref="Abstractions.Services.IMediaStreamService.OpenAsync"/>, with
    /// <paramref name="words"/> painted over the picture.</summary>
    /// <param name="backgroundPath">A clip to play under the words when the source has no picture of
    /// its own; null, or a path that is not there, puts them over black.</param>
    Task<MediaStreamSession> OpenBurningInAsync(
        string filePath,
        TimeSpan startOffset,
        int pitch,
        int tempo,
        AudioMix? mix,
        TimedLyrics words,
        string? backgroundPath,
        CancellationToken cancellationToken = default);
}
