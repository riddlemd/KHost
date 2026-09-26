using KHost.Abstractions.Models;

namespace KHost.Domain.Services.Displays.LocalScreen;

/// <summary>How long a screen holds a song back before its start, so a singer whose words begin
/// almost at once is led in rather than rushed.</summary>
/// <remarks>Domain, not Common: the grace is this host's own setting, which no plugin display can
/// read, so a shared helper would publish a rule nothing outside the host could apply.</remarks>
public static class LeadInGrace
{
    /// <summary>The longest grace honoured, whatever a hand-edited setting says.</summary>
    public const double MaxSeconds = 10;

    /// <summary>Seconds to hold before the song's zero: the grace less the run-up the song already
    /// has before its first words, and never less than zero.</summary>
    /// <remarks>Zero for a song with no timing or no pages, since only words the screen draws
    /// itself are held for. The first words are the earliest syllable, or the first page where
    /// the timing has no syllables at all.</remarks>
    public static double PreRollSeconds(TimedLyrics? lyrics, double graceSeconds)
    {
        if (lyrics is not { Pages.Count: > 0 }) return 0;

        var grace = Math.Min(graceSeconds, MaxSeconds);

        return Math.Max(0, grace - Math.Max(0, FirstWordsSeconds(lyrics)));
    }

    private static double FirstWordsSeconds(TimedLyrics lyrics)
    {
        var syllables = lyrics.Pages
            .SelectMany(page => page.Lines)
            .SelectMany(line => line.Syllables)
            .Select(syllable => syllable.StartSeconds)
            .ToList();

        return syllables.Count > 0 ? syllables.Min() : lyrics.Pages.Min(page => page.ShowFromSeconds);
    }
}
