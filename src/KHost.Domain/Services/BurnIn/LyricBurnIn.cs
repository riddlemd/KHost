using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

namespace KHost.Domain.Services.BurnIn;

/// <summary>Opens a song's stream with its words burned in, for a display that cannot draw them.
/// </summary>
/// <remarks>Driven by the contract alone: any song some <see cref="ITimedLyricsProvider"/> times gets
/// its words painted into the encode, whoever supplied them, and the host learns nothing of their
/// format. A song with no timed words is not this class's to open. What the words go over is
/// <see cref="SongBackdrops.ForPlaying"/>'s answer, never the venue's song backgrounds.</remarks>
public sealed class LyricBurnIn(ITimedLyricsService lyrics, IBurnInStreamService streams)
{
    /// <summary>The request's stream with its words burned in, or null when the song has none to
    /// burn in.</summary>
    public async Task<MediaStreamSession?> OpenAsync(MediaRenderRequest request, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(request.FilePath, cancellationToken) is not { } words) return null;

        return await streams.OpenBurningInAsync(
            request.FilePath,
            request.StartOffset,
            request.Pitch,
            request.Tempo,
            request.Mix,
            words,
            cancellationToken);
    }

    /// <summary>The request's stream for a display that draws the words itself, or null when the song
    /// has none, whose picture is then whatever the file carries.</summary>
    /// <remarks>Under timed words the picture is the source's own moving one or nothing: never a
    /// cover, never anything from an audio file (<see cref="SongBackdrops"/>).</remarks>
    public async Task<MediaStreamSession?> OpenUnderDrawnWordsAsync(
        MediaRenderRequest request, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(request.FilePath, cancellationToken) is null) return null;

        return await streams.OpenUnderDrawnWordsAsync(
            request.FilePath,
            request.StartOffset,
            request.Pitch,
            request.Tempo,
            request.Mix,
            cancellationToken);
    }

    /// <summary>The song's timed words, or null when it has none to burn in.</summary>
    public async Task<TimedLyrics?> FindAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var words = await lyrics.GetTimedLyricsAsync(filePath, cancellationToken);
        return words is { Pages.Count: > 0 } ? words : null;
    }
}
