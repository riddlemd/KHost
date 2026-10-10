using KHost.Abstractions.Models;

namespace KHost.Domain.Services;

/// <summary>Moves a song's words against its music, for a display whose picture runs ahead of or
/// behind its sound (some wireless screens do).</summary>
/// <remarks>Domain, not Common: the offset is this host's own setting, applied once where the screen
/// and the burn-in both read the words, so no display needs to know it exists.</remarks>
public static class LyricsOffset
{
    /// <summary>The largest offset honoured either way, whatever a hand-edited setting says.</summary>
    public const int MaxMilliseconds = 2000;

    public static int ClampMilliseconds(int milliseconds) => Math.Clamp(milliseconds, -MaxMilliseconds, MaxMilliseconds);

    /// <summary>Every moment in the timing moved by <paramref name="seconds"/>; positive is later.</summary>
    /// <remarks>The song's length is not a moment and stays. A time moved before zero is kept as it
    /// is: the words were already due when the song starts.</remarks>
    public static TimedLyrics Shift(TimedLyrics lyrics, double seconds)
    {
        if (seconds == 0) return lyrics;

        return lyrics with
        {
            Pages = [.. lyrics.Pages.Select(page => page with
            {
                ShowFromSeconds = page.ShowFromSeconds + seconds,
                ShowUntilSeconds = page.ShowUntilSeconds + seconds,
                Lines = [.. page.Lines.Select(line => line with
                {
                    Syllables = [.. line.Syllables.Select(syllable => syllable with
                    {
                        StartSeconds = syllable.StartSeconds + seconds,
                        EndSeconds = syllable.EndSeconds + seconds,
                    })],
                    LeadIn = line.LeadIn is { } leadIn ? leadIn with { StartSeconds = leadIn.StartSeconds + seconds } : null,
                })],
            })],
            CountIns = [.. lyrics.CountIns.Select(countIn => countIn with
            {
                StartSeconds = countIn.StartSeconds + seconds,
                EndSeconds = countIn.EndSeconds + seconds,
            })],
        };
    }
}
