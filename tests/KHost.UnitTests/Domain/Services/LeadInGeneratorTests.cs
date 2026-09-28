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
}
