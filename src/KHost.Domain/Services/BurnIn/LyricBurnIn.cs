using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.BurnIn;

/// <summary>Opens a song's stream with its words burned in, for a display that cannot draw them.
/// </summary>
/// <remarks>Driven by the contract alone: any song some <see cref="ITimedLyricsProvider"/> times gets
/// its words painted into the encode, whoever supplied them, and the host learns nothing of their
/// format. A song with no timed words is not this class's to open.</remarks>
public sealed class LyricBurnIn(
    ITimedLyricsService lyrics,
    IVenuesService venues,
    IBackgroundPackService backgrounds,
    IBurnInStreamService streams,
    ILogger<LyricBurnIn> logger)
{
    /// <summary>Which of the venue's backgrounds a song gets, given how many are on offer.</summary>
    /// <remarks>A seam so a test can pin the pick; asserting a filter over a random choice is
    /// otherwise true only most of the time.</remarks>
    internal Func<int, int> PickBackgroundIndex { get; init; } = Random.Shared.Next;

    /// <summary>The request's stream with its words burned in, or null when the song has none to
    /// burn in.</summary>
    public async Task<MediaStreamSession?> OpenAsync(MediaRenderRequest request, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(request.FilePath, cancellationToken) is not { } found) return null;

        return await streams.OpenBurningInAsync(
            request.FilePath,
            request.StartOffset,
            request.Pitch,
            request.Tempo,
            request.Mix,
            found.Words,
            found.BackgroundPath,
            cancellationToken);
    }

    /// <summary>The song's timed words and the background to put them over, or null when it has no
    /// words to burn in.</summary>
    public async Task<(TimedLyrics Words, string? BackgroundPath)?> FindAsync(
        string filePath, CancellationToken cancellationToken = default)
    {
        var words = await lyrics.GetTimedLyricsAsync(filePath, cancellationToken);
        if (words is not { Pages.Count: > 0 }) return null;

        return (words, await PickBackgroundAsync(cancellationToken));
    }

    /// <summary>One of the venue's chosen song backgrounds, or null for black.</summary>
    /// <remarks>Picked per song, so a night varies. Every way of having none — no venue, none
    /// ticked, the folder gone, a ticked clip deleted — answers null: a background is decoration and
    /// must never be why a song fails to play.</remarks>
    private async Task<string?> PickBackgroundAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await venues.ReadSelectedVenueAsync() is not { } venue) return null;

            var chosen = venue.Settings.SongBackgrounds;
            if (chosen.Count == 0) return null;

            var pack = await backgrounds.ReadAsync(cancellationToken);

            // Matched against what is in the folder rather than joined onto it, so a choice left
            // behind by a deleted clip resolves to nothing instead of a bad path.
            var candidates = pack.Entries
                .Where(entry => chosen.Contains(entry.File, StringComparer.OrdinalIgnoreCase))
                .ToList();

            return candidates.Count == 0 ? null : candidates[PickBackgroundIndex(candidates.Count)].FilePath;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not read the venue's song backgrounds");
            return null;
        }
    }
}
