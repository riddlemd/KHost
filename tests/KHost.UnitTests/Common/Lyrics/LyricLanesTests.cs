using KHost.Abstractions.Models;
using KHost.Common.Lyrics;

namespace KHost.UnitTests.Common.Lyrics;

/// <summary>Who sings where, as the console's lane timeline reads it off a song's timing.</summary>
public class LyricLanesTests
{
    private static readonly LyricColor Blue = new(0x00, 0x80, 0xFF);
    private static readonly LyricColor Green = new(0x11, 0xA8, 0x00);

    private static readonly TimeSpan TwoSeconds = TimeSpan.FromSeconds(2);

    [Fact]
    public void SungSpansByVoice_TwoSingers_GivesALaneEach()
    {
        var lanes = LyricLanes.SungSpansByVoice(Lyrics(
            Page("♂", Blue, Line(1, 3)),
            Page("♀", Green, Line(5, 7))));

        Assert.Equal(["♂", "♀"], lanes.Select(lane => lane.Voice));
        Assert.Equal([new SungSpan(1, 3)], lanes[0].Spans);
        Assert.Equal([new SungSpan(5, 7)], lanes[1].Spans);
    }

    /// <summary>A stretch runs from a line's first syllable to its last, not from the page's
    /// window.</summary>
    [Fact]
    public void SungSpansByVoice_ALine_SpansItsFirstSyllableToItsLast()
    {
        var page = new LyricPage
        {
            ShowFromSeconds = 0,
            ShowUntilSeconds = 60,
            Voice = "a",
            Lines = [new LyricLine { Syllables = [new(10, 10.5, "la"), new(10.5, 11, "la"), new(11, 12.25, "la")] }],
        };

        var lane = Assert.Single(LyricLanes.SungSpansByVoice(Lyrics(page)));

        Assert.Equal([new SungSpan(10, 12.25)], lane.Spans);
    }

    [Fact]
    public void SungSpansByVoice_LinesCloserThanTheGap_MergeIntoOneSpan()
    {
        var lane = Assert.Single(LyricLanes.SungSpansByVoice(
            Lyrics(Page("a", Blue, Line(0, 2), Line(3.9, 6))), TwoSeconds));

        Assert.Equal([new SungSpan(0, 6)], lane.Spans);
    }

    [Fact]
    public void SungSpansByVoice_LinesAtLeastTheGapApart_StaySeparate()
    {
        var lane = Assert.Single(LyricLanes.SungSpansByVoice(
            Lyrics(Page("a", Blue, Line(0, 2), Line(4, 6))), TwoSeconds));

        Assert.Equal([new SungSpan(0, 2), new SungSpan(4, 6)], lane.Spans);
    }

    /// <summary>Merging is per singer, across pages: a singer's next page a breath later is still
    /// one section.</summary>
    [Fact]
    public void SungSpansByVoice_OneSingersPagesAcrossAnotherSingers_MergeOnlyWithinTheirOwnLane()
    {
        var lanes = LyricLanes.SungSpansByVoice(Lyrics(
            Page("a", Blue, Line(0, 2)),
            Page("b", Green, Line(20, 22)),
            Page("a", Blue, Line(3, 5))), TwoSeconds);

        Assert.Equal([new SungSpan(0, 5)], lanes.Single(lane => lane.Voice == "a").Spans);
    }

    /// <summary>A hand-off where both sing at once stays two lanes, not one bar recoloured.</summary>
    [Fact]
    public void SungSpansByVoice_OverlappingSingers_StayInSeparateLanes()
    {
        var lanes = LyricLanes.SungSpansByVoice(Lyrics(
            Page("a", Blue, Line(0, 10)),
            Page("b", Green, Line(8, 20))));

        Assert.Equal(2, lanes.Count);
        Assert.Equal([new SungSpan(0, 10)], lanes[0].Spans);
        Assert.Equal([new SungSpan(8, 20)], lanes[1].Spans);
    }

    [Fact]
    public void SungSpansByVoice_PagesNamingNobody_FormOneLaneOfTheirOwn()
    {
        var lanes = LyricLanes.SungSpansByVoice(Lyrics(
            Page(null, Blue, Line(0, 2)),
            Page("a", Green, Line(30, 32)),
            Page(null, Blue, Line(60, 62))));

        Assert.Equal([null, "a"], lanes.Select(lane => lane.Voice));
        Assert.Equal([new SungSpan(0, 2), new SungSpan(60, 62)], lanes[0].Spans);
    }

    [Fact]
    public void SungSpansByVoice_NothingSung_GivesNoLanes()
    {
        var silent = new LyricPage { ShowFromSeconds = 0, ShowUntilSeconds = 5, Voice = "a", Lines = [new LyricLine()] };

        Assert.Empty(LyricLanes.SungSpansByVoice(Lyrics()));
        Assert.Empty(LyricLanes.SungSpansByVoice(Lyrics(silent)));
    }

    [Fact]
    public void SungSpansByVoice_PagesInSeveralColours_TakesTheColourMostPagesCarry()
    {
        var lane = Assert.Single(LyricLanes.SungSpansByVoice(Lyrics(
            Page("a", Green, Line(0, 1)),
            Page("a", Blue, Line(10, 11)),
            Page("a", Blue, Line(20, 21)))));

        Assert.Equal(Blue, lane.Color);
    }

    [Fact]
    public void SungSpansByVoice_NoPageCarriesAColour_LeavesTheLaneUncoloured()
    {
        var lane = Assert.Single(LyricLanes.SungSpansByVoice(Lyrics(Page("a", null, Line(0, 1)))));

        Assert.Null(lane.Color);
    }

    /// <summary>Ordered by who is heard first, not by who has the first page in the document.</summary>
    [Fact]
    public void SungSpansByVoice_ASingerHeardFirstOnALaterPage_ComesFirst()
    {
        var lanes = LyricLanes.SungSpansByVoice(Lyrics(
            Page("late", Blue, Line(50, 52)),
            Page("early", Green, Line(5, 7))));

        Assert.Equal(["early", "late"], lanes.Select(lane => lane.Voice));
    }

    /// <summary>The default sits between Daddy Cool's longest breath inside a section (1.94s) and
    /// its shortest instrumental break (15.7s).</summary>
    [Fact]
    public void SungSpansByVoice_NoGapNamed_MergesABreathButNotAnInstrumentalBreak()
    {
        var lane = Assert.Single(LyricLanes.SungSpansByVoice(Lyrics(
            Page("a", Blue, Line(47, 50), Line(51.94, 55)),
            Page("a", Blue, Line(70.8, 72.5), Line(89.6, 91.8)))));

        Assert.Equal([new SungSpan(47, 55), new SungSpan(70.8, 72.5), new SungSpan(89.6, 91.8)], lane.Spans);
    }

    [Fact]
    public void SungSpansAsOneLane_TwoSingers_MergesEveryStretchIntoOneUncolouredLane()
    {
        var lane = LyricLanes.SungSpansAsOneLane(Lyrics(
            Page("a", Blue, Line(0, 10)),
            Page("b", Green, Line(11, 20)),
            Page("a", Blue, Line(40, 42))), TwoSeconds);

        Assert.NotNull(lane);
        Assert.Null(lane.Voice);
        Assert.Null(lane.Color);
        Assert.Equal([new SungSpan(0, 20), new SungSpan(40, 42)], lane.Spans);
    }

    [Fact]
    public void SungSpansAsOneLane_NothingSung_IsNull()
        => Assert.Null(LyricLanes.SungSpansAsOneLane(Lyrics()));

    private static TimedLyrics Lyrics(params LyricPage[] pages) => new()
    {
        DurationSeconds = 240,
        Bounds = new LyricBox(0, 0, 640, 360),
        Pages = pages,
    };

    private static LyricPage Page(string? voice, LyricColor? active, params LyricLine[] lines) => new()
    {
        ShowFromSeconds = 0,
        ShowUntilSeconds = 240,
        Voice = voice,
        Active = active,
        Lines = lines,
    };

    private static LyricLine Line(double start, double end)
        => new() { Syllables = [new(start, (start + end) / 2, "la"), new((start + end) / 2, end, "la")] };
}
