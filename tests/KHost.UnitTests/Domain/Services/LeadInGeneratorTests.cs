using KHost.Abstractions.Models;
using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

/// <summary>Which lines the host gives a lead-in to, and where it sets off from.</summary>
public class LeadInGeneratorTests
{
    private static readonly LyricBox Bounds = new(0, 0, 640, 360);

    private static LyricLine Line(double start, double end, double x = 100, LyricLeadIn? leadIn = null, bool placed = true) => new()
    {
        Position = placed ? new LyricBox(x, 100, 400, 50) : null,
        Syllables = [new LyricSyllable(start, end, "la")],
        LeadIn = leadIn,
    };

    /// <summary>A placed line of several syllables, each given as its start and end.</summary>
    private static LyricLine Words(double x, LyricLeadIn? leadIn, params (double Start, double End)[] syllables) => new()
    {
        Position = new LyricBox(x, 100, 400, 50),
        Syllables = [.. syllables.Select(s => new LyricSyllable(s.Start, s.End, "la "))],
        LeadIn = leadIn,
    };

    private static LyricLine Words(params (double Start, double End)[] syllables) => Words(100, null, syllables);

    private static LyricPage Page(double showFrom, params LyricLine[] lines) => new()
    {
        ShowFromSeconds = showFrom,
        ShowUntilSeconds = 999,
        Lines = lines,
    };

    private static TimedLyrics Song(LyricPage[] pages, bool rightToLeft = false) => new()
    {
        DurationSeconds = 999,
        Bounds = Bounds,
        IsRightToLeft = rightToLeft,
        Pages = pages,
    };

    private static TimedLyrics Fill(TimedLyrics lyrics) => LeadInGenerator.AddMissing(lyrics, LeadInGenerator.DefaultLongPauseSeconds);

    private static LyricLeadIn? LeadInOf(TimedLyrics lyrics, int page, int line) => lyrics.Pages[page].Lines[line].LeadIn;

    [Fact]
    public void AddMissing_PageOpener_SetsOffTheLongestRunBeforeItsFirstSyllable()
    {
        // A pause too short for any other line: only being a page's opener earns it one.
        var lyrics = Fill(Song([Page(0, Line(1, 13.5)), Page(13, Line(15, 16))]));

        Assert.Equal(new LyricLeadIn(15 - LeadInGenerator.MaxRunSeconds, 100 - 640 * LeadInGenerator.RunWidthFraction), LeadInOf(lyrics, 1, 0));
    }

    [Fact]
    public void AddMissing_PageOpener_SungStraightAfterThePageBefore_GetsNone()
    {
        var lyrics = Fill(Song([Page(0, Line(1, 14.5)), Page(13, Line(15, 16))]));

        Assert.Null(LeadInOf(lyrics, 1, 0));
    }

    [Fact]
    public void AddMissing_ThePauseSetting_DecidesWhichSilenceIsLong()
    {
        var song = Song([Page(0, Line(1, 2), Line(4.5, 6))]);

        Assert.NotNull(LeadInGenerator.AddMissing(song, 2).Pages[0].Lines[1].LeadIn);
        Assert.Null(LeadInGenerator.AddMissing(song, 3).Pages[0].Lines[1].LeadIn);
    }

    [Fact]
    public void AddMissing_PageOpener_NeverSetsOffBeforeThePageIsShown()
    {
        var lyrics = Fill(Song([Page(14, Line(15, 16))]));

        Assert.Equal(14, LeadInOf(lyrics, 0, 0)!.StartSeconds);
    }

    [Fact]
    public void AddMissing_PageOpener_ShownTooLateForAVisibleRun_GetsNone()
    {
        var lyrics = Fill(Song([Page(14.8, Line(15, 16))]));

        Assert.Null(LeadInOf(lyrics, 0, 0));
    }

    [Fact]
    public void AddMissing_LineAfterALongPause_GetsOne()
    {
        var lyrics = Fill(Song([Page(0, Line(1, 2), Line(2 + LeadInGenerator.DefaultLongPauseSeconds, 6))]));

        Assert.Equal(5 - LeadInGenerator.MaxRunSeconds, LeadInOf(lyrics, 0, 1)!.StartSeconds);
    }

    [Fact]
    public void AddMissing_LineAfterAShortPause_GetsNone()
    {
        var lyrics = Fill(Song([Page(0, Line(1, 2), Line(4.9, 6))]));

        Assert.Null(LeadInOf(lyrics, 0, 1));
    }

    [Fact]
    public void AddMissing_LongPauseMeasuredFromTheLatestEnd_NotTheLatestStart()
    {
        // A held note from an earlier line outlasts a short one after it, so the room was never silent.
        var lyrics = Fill(Song([Page(0, Line(1, 9), Line(2, 3), Line(7, 8))]));

        Assert.Null(LeadInOf(lyrics, 0, 2));
    }

    [Fact]
    public void AddMissing_KeepsALeadInTheTimingSupplied()
    {
        var supplied = new LyricLeadIn(12.5, 42);

        var lyrics = Fill(Song([Page(10, Line(15, 16, leadIn: supplied))]));

        Assert.Same(supplied, LeadInOf(lyrics, 0, 0));
    }

    [Fact]
    public void AddMissing_LineWithNoPosition_GetsNone()
    {
        var lyrics = Fill(Song([Page(10, Line(15, 16, placed: false))]));

        Assert.Null(LeadInOf(lyrics, 0, 0));
    }

    [Fact]
    public void AddMissing_LineHardAgainstTheLeadingEdge_RunsOnlyAsFarAsTheFrameAllows()
    {
        var lyrics = Fill(Song([Page(10, Line(15, 16, x: 10))]));

        Assert.Equal(0, LeadInOf(lyrics, 0, 0)!.X);
    }

    [Fact]
    public void AddMissing_RightToLeft_MeasuresRoomOnTheRightOfTheLine()
    {
        // Box spans 230..630: ten units of room on the right, however much there is on the left.
        var lyrics = Fill(Song([Page(10, Line(15, 16, x: 230))], rightToLeft: true));

        Assert.Equal(220, LeadInOf(lyrics, 0, 0)!.X);
    }

    // The line under test follows an opener sung straight into it, so its own start never earns one.

    [Fact]
    public void AddMissing_PauseInsideALine_AtTheSetting_LeadsInToTheWordAfterIt()
    {
        var lyrics = Fill(Song([Page(0, Line(1, 2), Words((2.5, 3), (6, 7)))]));

        Assert.Equal(new LyricLeadIn(6 - LeadInGenerator.MaxRunSeconds, 100 - 640 * LeadInGenerator.RunWidthFraction) { ArriveAtSyllable = 1 }, LeadInOf(lyrics, 0, 1));
    }

    [Fact]
    public void AddMissing_PauseInsideALine_ShorterThanTheSetting_GetsNone()
    {
        var lyrics = Fill(Song([Page(0, Line(1, 2), Words((2.5, 3), (5.9, 7)))]));

        Assert.Null(LeadInOf(lyrics, 0, 1));
    }

    [Fact]
    public void AddMissing_PauseInsideALine_ThePauseSettingDecides()
    {
        // The gap a room hears in "turns ... blue": 2.93s.
        var song = Song([Page(0, Line(1, 2), Words((2.5, 20.90), (23.83, 24.5)))]);

        Assert.Equal(1, LeadInGenerator.AddMissing(song, 2).Pages[0].Lines[1].LeadIn!.ArriveAtSyllable);
        Assert.Null(LeadInGenerator.AddMissing(song, 3).Pages[0].Lines[1].LeadIn);
    }

    [Fact]
    public void AddMissing_PauseInsideALine_ShorterThanTheLongestRun_RunsForThePause()
    {
        var lyrics = LeadInGenerator.AddMissing(Song([Page(0, Line(1, 2), Words((2.5, 3), (4.2, 5)))]), 1);

        Assert.Equal(3, LeadInOf(lyrics, 0, 1)!.StartSeconds, 9);
    }

    [Fact]
    public void AddMissing_LineStartAndAPauseInsideItBothEarnOne_TheLineStartKeepsIt()
    {
        var lyrics = Fill(Song([Page(0, Line(1, 2), Words((5, 6), (9.5, 10)))]));

        Assert.Equal(0, LeadInOf(lyrics, 0, 1)!.ArriveAtSyllable);
        Assert.Equal(3, LeadInOf(lyrics, 0, 1)!.StartSeconds);
    }

    [Fact]
    public void AddMissing_SeveralPausesInsideALine_TheFirstGetsIt()
    {
        var lyrics = Fill(Song([Page(0, Line(1, 2), Words((2.5, 3), (3.2, 3.5), (6.5, 7), (10.5, 11)))]));

        Assert.Equal(2, LeadInOf(lyrics, 0, 1)!.ArriveAtSyllable);
    }

    [Fact]
    public void AddMissing_PauseInsideALine_CoveredByAHeldNoteElsewhere_GetsNone()
    {
        var lyrics = Fill(Song([Page(0, Line(1, 9), Words((2.5, 3), (6, 7)))]));

        Assert.Null(LeadInOf(lyrics, 0, 1));
    }

    [Fact]
    public void AddMissing_PauseInsideALine_KeepsALeadInTheTimingSupplied()
    {
        var supplied = new LyricLeadIn(2, 42);

        var lyrics = Fill(Song([Page(0, Line(1, 2), Words(100, supplied, (2.5, 3), (6, 7)))]));

        Assert.Same(supplied, LeadInOf(lyrics, 0, 1));
    }

    [Fact]
    public void AddMissing_PauseInsideALineRightToLeft_MeasuresRoomOnTheRightOfTheLine()
    {
        var lyrics = Fill(Song([Page(0, Line(1, 2, x: 230), Words(230, null, (2.5, 3), (6, 7)))], rightToLeft: true));

        Assert.Equal(new LyricLeadIn(4, 220) { ArriveAtSyllable = 1 }, LeadInOf(lyrics, 0, 1));
    }
}
