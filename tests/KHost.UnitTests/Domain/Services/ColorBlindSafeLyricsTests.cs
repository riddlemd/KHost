using KHost.Abstractions.Models;
using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

/// <summary>Which colours a colour-blind viewer would confuse, and how they are moved apart. Expected
/// colours are the research script's own output for the same input, matched exactly.</summary>
public class ColorBlindSafeLyricsTests
{
    private static LyricColor Hex(string hex) => new(
        Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));

    private static LyricColor? MaybeHex(string? hex) => hex is null ? null : Hex(hex);

    private static LyricPage Page(string? voice, double from, double until, string? active, string? inactive) => new()
    {
        ShowFromSeconds = from,
        ShowUntilSeconds = until,
        Voice = voice,
        Active = MaybeHex(active),
        Inactive = MaybeHex(inactive),
    };

    private static LyricCountIn CountIn(string? active, string? inactive) => new()
    {
        StartSeconds = 0,
        EndSeconds = 2,
        Position = new LyricBox(0, 0, 10, 10),
        Active = MaybeHex(active),
        Inactive = MaybeHex(inactive),
        Border = Hex("#000000"),
    };

    private static TimedLyrics Song(params LyricPage[] pages) => new()
    {
        DurationSeconds = 60,
        Bounds = new LyricBox(0, 0, 640, 360),
        Pages = pages,
    };

    /// <summary>Two singers on screen together, as a duet's pages overlap.</summary>
    private static TimedLyrics Duet(string a, string? activeA, string? inactiveA, string b, string? activeB, string? inactiveB)
        => Song(Page(a, 0, 10, activeA, inactiveA), Page(b, 5, 15, activeB, inactiveB));

    private static (LyricColor Active, LyricColor Inactive) Seen(TimedLyrics lyrics, string? voice)
    {
        var page = lyrics.Pages.First(p => p.Voice == voice);
        return (page.Active ?? ColorBlindSafeLyrics.ThemeActive, page.Inactive ?? ColorBlindSafeLyrics.ThemeInactive);
    }

    [Fact]
    public void Separate_ASongInTheThemeColours_IsReturnedAsItIs()
    {
        var lyrics = Song(Page(null, 0, 10, null, null));

        Assert.Same(lyrics, ColorBlindSafeLyrics.Separate(lyrics));
    }

    [Fact]
    public void Separate_ADuetNobodyWouldConfuse_IsReturnedAsItIs()
    {
        // Red against purple, pale orange against pale purple: apart under every sight.
        var lyrics = Duet("Freddie", "#FF3300", "#FFE1D5", "(Backing vocals)", "#6831F9", "#E0D6FE");

        Assert.Same(lyrics, ColorBlindSafeLyrics.Separate(lyrics));
    }

    [Fact]
    public void Separate_SingersNeverOnScreenTogether_AreNotComparedWithEachOther()
    {
        // The same pale tints that fail side by side, on pages that never overlap.
        var lyrics = Song(Page("Kid Rock", 0, 10, "#0B96CA", "#D7F2FD"), Page("Sherly Crow", 10, 20, "#F52C77", "#FDD7E6"));

        Assert.Same(lyrics, ColorBlindSafeLyrics.Separate(lyrics));
    }

    /// <summary>Library duets the survey found at risk: pale blue against pale pink (protan, deutan),
    /// green against red (deutan), pale green against pale blue (tritan).</summary>
    [Theory]
    [InlineData("Kid Rock", "#0B96CA", "#D7F2FD", "Sherly Crow", "#F52C77", "#FDD7E6", "#0B96CA", "#FBFEFF", "#F52C77", "#E2BDCC")]
    [InlineData("Lisa", "#FF0000", "#FFFFFF", "(Adlib)", "#009D00", "#D2FFD2", "#FF0000", "#FFFFFF", "#57D450", "#D2FFD2")]
    [InlineData("♂", "#0080FF", "#D5EAFF", "♀", "#11A800", "#D9FFD5", "#0080FF", "#CBE0F5", "#11A800", "#F8FFF7")]
    public void Separate_ADuetAtRisk_MatchesTheResearchRuleExactly(
        string a, string activeA, string inactiveA, string b, string activeB, string inactiveB,
        string expectedActiveA, string expectedInactiveA, string expectedActiveB, string expectedInactiveB)
    {
        var fixedUp = ColorBlindSafeLyrics.Separate(Duet(a, activeA, inactiveA, b, activeB, inactiveB));

        Assert.Equal((Hex(expectedActiveA), Hex(expectedInactiveA)), Seen(fixedUp, a));
        Assert.Equal((Hex(expectedActiveB), Hex(expectedInactiveB)), Seen(fixedUp, b));
        AssertNothingAtRisk(fixedUp);
    }

    [Fact]
    public void Separate_AWipeAtRisk_LightensTheUnsungWords_ThenDarkensTheSung()
    {
        // A pale sung pink over a paler unsung one: the unsung words go to white first.
        var fixedUp = ColorBlindSafeLyrics.Separate(Song(Page(null, 0, 10, "#FFD0D0", "#FFE8E8")));

        Assert.Equal((Hex("#F1C3C3"), Hex("#FFFFFF")), Seen(fixedUp, null));
        AssertNothingAtRisk(fixedUp);
    }

    [Fact]
    public void Separate_ThreeSingers_MovesTheConfusedTints_AndLeavesEveryOtherPairSafe()
    {
        // Sage's pale pink is confused with both others' tints; the wipes share those tints and
        // must still be told apart once they move.
        var lyrics = Song(
            Page("Sage", 0, 10, "#FB39A9", "#FED6ED"),
            Page("(Adlib)", 5, 15, "#03BC60", "#D6FEEA"),
            Page("Desi", 8, 20, "#6464FF", "#D5D5FF"));

        var fixedUp = ColorBlindSafeLyrics.Separate(lyrics);

        Assert.Equal((Hex("#FB39A9"), Hex("#D1C5CC")), Seen(fixedUp, "Sage"));
        Assert.Equal((Hex("#03BC60"), Hex("#F8FFFB")), Seen(fixedUp, "(Adlib)"));
        Assert.Equal((Hex("#6464FF"), Hex("#C5C5EE")), Seen(fixedUp, "Desi"));
        AssertNothingAtRisk(fixedUp);
    }

    [Fact]
    public void Separate_MovesACountIn_WithTheVoiceWhosePairItRepeats()
    {
        var lyrics = Duet("Kid Rock", "#0B96CA", "#D7F2FD", "Sherly Crow", "#F52C77", "#FDD7E6") with
        {
            CountIns = [CountIn("#F52C77", "#FDD7E6"), CountIn("#123456", "#FDD7E6")],
        };

        var fixedUp = ColorBlindSafeLyrics.Separate(lyrics);

        Assert.Equal(Hex("#E2BDCC"), fixedUp.CountIns[0].Inactive);
        Assert.Equal(Hex("#F52C77"), fixedUp.CountIns[0].Active);
        // A pair no voice uses is not a singer's, so nothing says it moved.
        Assert.Same(lyrics.CountIns[1], fixedUp.CountIns[1]);
    }

    [Fact]
    public void Separate_AnUnsetColourThatDoesNotMove_StaysUnset()
    {
        var fixedUp = ColorBlindSafeLyrics.Separate(Duet("Lisa", "#FF0000", null, "(Adlib)", "#009D00", "#D2FFD2"));

        Assert.Null(fixedUp.Pages.Single(p => p.Voice == "Lisa").Inactive);
        Assert.Equal(Hex("#57D450"), fixedUp.Pages.Single(p => p.Voice == "(Adlib)").Active);
    }

    [Fact]
    public void Separate_AnUnsetColourThatHasToMove_IsSetExplicitly()
    {
        // A left to the theme, B a shade off it: something of A's must change, and says so.
        var lyrics = Duet("A", null, null, "B", "#8659FA", "#FEFEFE");
        var fixedUp = ColorBlindSafeLyrics.Separate(lyrics);

        var a = fixedUp.Pages.Single(p => p.Voice == "A");
        Assert.True(a.Active is not null || a.Inactive is not null);
        if (a.Active is { } active) Assert.NotEqual(ColorBlindSafeLyrics.ThemeActive, active);
        if (a.Inactive is { } inactive) Assert.NotEqual(ColorBlindSafeLyrics.ThemeInactive, inactive);
        AssertNothingAtRisk(fixedUp);
    }

    [Fact]
    public void Separate_GivesTheSameColoursEveryTime_WhicheverSingerAppearsFirst()
    {
        var first = ColorBlindSafeLyrics.Separate(Duet("A", "#0B96CA", "#D7F2FD", "B", "#F52C77", "#FDD7E6"));
        var again = ColorBlindSafeLyrics.Separate(Duet("A", "#0B96CA", "#D7F2FD", "B", "#F52C77", "#FDD7E6"));
        var swapped = ColorBlindSafeLyrics.Separate(Duet("B", "#F52C77", "#FDD7E6", "A", "#0B96CA", "#D7F2FD"));

        Assert.NotEqual(Seen(Duet("A", "#0B96CA", "#D7F2FD", "B", "#F52C77", "#FDD7E6"), "A"), Seen(first, "A"));
        Assert.Equal(Seen(first, "A"), Seen(again, "A"));
        Assert.Equal(Seen(first, "B"), Seen(again, "B"));
        Assert.Equal(Seen(first, "A"), Seen(swapped, "A"));
        Assert.Equal(Seen(first, "B"), Seen(swapped, "B"));
    }

    /// <summary>The media gave two singers one colour, so they are meant to read as one singer.</summary>
    [Fact]
    public void Separate_SingersInTheSameColours_AreLeftAlike()
    {
        var lyrics = Duet("A", "#0B96CA", "#FFFFFF", "B", "#0B96CA", "#FFFFFF");

        Assert.Same(lyrics, ColorBlindSafeLyrics.Separate(lyrics));
    }

    [Fact]
    public void Separate_WhenLightnessCannotSeparateThem_FallsBackToOkabeItoInVoiceOrder()
    {
        // Three singers a shade apart: however two are pushed apart, the third lands on one of them.
        var fixedUp = ColorBlindSafeLyrics.Separate(Song(
            Page("A", 0, 10, "#8558FA", "#FFFFFF"), Page("B", 0, 10, "#8559FA", "#FFFFFE"), Page("C", 0, 10, "#8658FA", "#FEFFFF")));

        Assert.Equal((Hex("#0072B2"), Hex("#D4EBFF")), Seen(fixedUp, "A"));
        Assert.Equal((Hex("#E69F00"), Hex("#FFE3BC")), Seen(fixedUp, "B"));
        Assert.Equal((Hex("#CC79A7"), Hex("#FFDDEE")), Seen(fixedUp, "C"));
    }

    [Fact]
    public void Fallback_WrapsThePalette_PastSevenVoices()
    {
        var palette = ColorBlindSafeLyrics.Fallback([.. Enumerable.Range(0, 8).Select(n => $"v{n}")]);

        Assert.Equal(Hex("#0072B2"), palette["v0"][0]);
        Assert.Equal(Hex("#0072B2"), palette["v7"][0]);
        Assert.Equal(Hex("#D4EBFF"), palette["v0"][1]);
    }

    [Fact]
    public void PushApart_NeverDarkensBelowTheFloor()
    {
        // White cannot rise, so everything rests on the other colour coming down.
        var (up, down) = ColorBlindSafeLyrics.PushApart(Hex("#FFFFFF"), Hex("#FFFFFF"));

        Assert.Equal(Hex("#FFFFFF"), up);
        Assert.True(ColorVision.Luminance(down) >= ColorBlindSafeLyrics.LuminanceFloor);
        Assert.False(ColorBlindSafeLyrics.IsAtRisk(up, down));
    }

    private static void AssertNothingAtRisk(TimedLyrics lyrics)
    {
        var pairs = lyrics.Pages.Select(p => (p.Voice, Colors: (p.Active ?? ColorBlindSafeLyrics.ThemeActive, p.Inactive ?? ColorBlindSafeLyrics.ThemeInactive))).ToArray();

        foreach (var (voice, (active, inactive)) in pairs)
            Assert.False(ColorBlindSafeLyrics.IsAtRisk(active, inactive), $"{voice}'s wipe");

        for (var i = 0; i < pairs.Length; i++)
        for (var j = i + 1; j < pairs.Length; j++)
        {
            Assert.False(ColorBlindSafeLyrics.IsAtRisk(pairs[i].Colors.Item1, pairs[j].Colors.Item1), $"{pairs[i].Voice}/{pairs[j].Voice} sung");
            Assert.False(ColorBlindSafeLyrics.IsAtRisk(pairs[i].Colors.Item2, pairs[j].Colors.Item2), $"{pairs[i].Voice}/{pairs[j].Voice} unsung");
        }
    }
}
