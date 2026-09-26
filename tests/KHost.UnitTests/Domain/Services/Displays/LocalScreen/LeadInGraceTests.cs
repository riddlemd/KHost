using KHost.Abstractions.Models;
using KHost.Domain.Services.Displays.LocalScreen;

namespace KHost.UnitTests.Domain.Services.Displays.LocalScreen;

public class LeadInGraceTests
{
    private static TimedLyrics WordsAt(params double[] syllableStarts) => new()
    {
        DurationSeconds = 200,
        Bounds = new LyricBox(0, 0, 640, 360),
        Pages =
        [
            new LyricPage
            {
                ShowFromSeconds = 0,
                ShowUntilSeconds = 30,
                Lines = [new LyricLine { Syllables = [.. syllableStarts.Select(s => new LyricSyllable(s, s + 0.4, "la"))] }],
            },
        ],
    };

    [Theory]
    [InlineData(5, 1.2, 3.8)]   // tops a short run-up up to the grace
    [InlineData(10, 12, 0)]     // an intro already longer is not delayed
    [InlineData(5, 0, 5)]       // words at the very start get the whole grace
    [InlineData(5, 5, 0)]       // words exactly at the grace need none
    [InlineData(5, 7.5, 0)]     // words after the grace need none
    [InlineData(0, 0, 0)]       // off holds nothing, even for words at once
    public void PreRollSeconds_TopsTheRunUpToTheGrace(double grace, double firstWords, double expected)
        => Assert.Equal(expected, LeadInGrace.PreRollSeconds(WordsAt(firstWords, firstWords + 3), grace), 9);

    /// <summary>The earliest syllable anywhere, not the first one listed.</summary>
    [Fact]
    public void PreRollSeconds_MeasuresFromTheEarliestSyllable()
        => Assert.Equal(4, LeadInGrace.PreRollSeconds(WordsAt(6, 1, 3), 5), 9);

    [Fact]
    public void PreRollSeconds_NoTiming_HoldsNothing()
        => Assert.Equal(0, LeadInGrace.PreRollSeconds(null, 10));

    [Fact]
    public void PreRollSeconds_ATimingWithNoPages_HoldsNothing()
        => Assert.Equal(0, LeadInGrace.PreRollSeconds(new TimedLyrics { DurationSeconds = 90, Bounds = new LyricBox(0, 0, 640, 360) }, 10));

    /// <summary>Pages with no syllables still have a first page to lead in to.</summary>
    [Fact]
    public void PreRollSeconds_PagesWithNoSyllables_MeasuresFromTheFirstPage()
    {
        var lyrics = new TimedLyrics
        {
            DurationSeconds = 90,
            Bounds = new LyricBox(0, 0, 640, 360),
            Pages = [new LyricPage { ShowFromSeconds = 8, ShowUntilSeconds = 9 }, new LyricPage { ShowFromSeconds = 2, ShowUntilSeconds = 4 }],
        };

        Assert.Equal(3, LeadInGrace.PreRollSeconds(lyrics, 5), 9);
    }

    /// <summary>A hand-edited setting cannot hold a room for a minute.</summary>
    [Fact]
    public void PreRollSeconds_AGraceBeyondTheMaximum_IsHeldToIt()
        => Assert.Equal(LeadInGrace.MaxSeconds, LeadInGrace.PreRollSeconds(WordsAt(0), 60), 9);

    /// <summary>A syllable timed before the song's zero is words at once, not a longer grace.</summary>
    [Fact]
    public void PreRollSeconds_WordsBeforeZero_CountAsWordsAtZero()
        => Assert.Equal(5, LeadInGrace.PreRollSeconds(WordsAt(-1), 5), 9);

    [Fact]
    public void PreRollSeconds_ANegativeGrace_HoldsNothing()
        => Assert.Equal(0, LeadInGrace.PreRollSeconds(WordsAt(0), -5));
}
