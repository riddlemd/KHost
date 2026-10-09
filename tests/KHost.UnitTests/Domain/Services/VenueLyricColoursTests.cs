using KHost.Abstractions.Models;
using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

public class VenueLyricColoursTests
{
    private static readonly LyricColor Sung = new(1, 2, 3), Unsung = new(4, 5, 6), Outline = new(7, 8, 9);
    private static readonly LyricColor Own = new(200, 100, 50);

    private static TimedLyrics Lyrics(LyricColor? active = null, LyricColor? inactive = null, LyricColor? border = null) => new()
    {
        DurationSeconds = 60,
        Bounds = new LyricBox(0, 0, 640, 360),
        Pages = [new LyricPage { ShowFromSeconds = 1, ShowUntilSeconds = 5, Active = active, Inactive = inactive }],
        CountIns = [new LyricCountIn { StartSeconds = 0, EndSeconds = 1, Position = new LyricBox(0, 0, 10, 10), Active = active, Inactive = inactive, Border = border }],
    };

    [Fact]
    public void FillUnset_ATimingNamingNoColours_TakesTheVenuesEverywhere()
    {
        var filled = VenueLyricColours.FillUnset(Lyrics(), Sung, Unsung, Outline);

        Assert.Equal((Sung, Unsung), (filled.Pages[0].Active, filled.Pages[0].Inactive));
        Assert.Equal((Sung, Unsung, Outline), (filled.CountIns[0].Active, filled.CountIns[0].Inactive, filled.CountIns[0].Border));
    }

    /// <summary>A colour the song's timing names is the song's choice, kept whatever the venue's are.</summary>
    [Fact]
    public void FillUnset_KeepsEveryColourTheTimingNames()
    {
        var filled = VenueLyricColours.FillUnset(Lyrics(Own, Own, Own), Sung, Unsung, Outline);

        Assert.Equal((Own, Own), (filled.Pages[0].Active, filled.Pages[0].Inactive));
        Assert.Equal((Own, Own, Own), (filled.CountIns[0].Active, filled.CountIns[0].Inactive, filled.CountIns[0].Border));
    }

    /// <summary>Nothing to fill comes back as the same instance, so a song with nothing at stake costs nothing.</summary>
    [Fact]
    public void FillUnset_NoVenueColours_ReturnsTheSameLyrics()
    {
        var lyrics = Lyrics();

        Assert.Same(lyrics, VenueLyricColours.FillUnset(lyrics, null, null, null));
    }

    /// <summary>A gap filled beside a colour the timing names leaves that colour alone.</summary>
    [Fact]
    public void FillUnset_OnlySomeColoursNamed_KeepsThoseAndFillsTheRest()
    {
        var filled = VenueLyricColours.FillUnset(Lyrics(active: Own), Sung, Unsung, Outline);

        Assert.Equal((Own, Unsung), (filled.Pages[0].Active, filled.Pages[0].Inactive));
        Assert.Equal((Own, Unsung, Outline), (filled.CountIns[0].Active, filled.CountIns[0].Inactive, filled.CountIns[0].Border));
    }

    [Fact]
    public void FillUnset_EverythingAlreadyColoured_ReturnsTheSameLyrics()
    {
        var lyrics = Lyrics(Own, Own, Own);

        Assert.Same(lyrics, VenueLyricColours.FillUnset(lyrics, Sung, Unsung, Outline));
    }

    /// <summary>Each colour fills only its own gap: a venue with only a sung colour leaves the rest to the screen.</summary>
    [Fact]
    public void FillUnset_OnlyASungColour_LeavesTheOthersUnset()
    {
        var filled = VenueLyricColours.FillUnset(Lyrics(), Sung, null, null);

        Assert.Equal((Sung, (LyricColor?)null), (filled.Pages[0].Active, filled.Pages[0].Inactive));
        Assert.Null(filled.CountIns[0].Border);
    }

    [Theory]
    [InlineData("#0a141e", 10, 20, 30)]
    [InlineData("#FFFFFF", 255, 255, 255)]
    public void FromHex_ReadsHex(string hex, byte r, byte g, byte b)
        => Assert.Equal(new LyricColor(r, g, b), VenueLyricColours.FromHex(hex));

    [Theory]
    [InlineData(null)]
    [InlineData("#fff")]
    [InlineData("0a141e0")]
    [InlineData("#0a141e0")]
    [InlineData("#zz141e")]
    public void FromHex_AnythingElse_IsNull(string? notHex)
        => Assert.Null(VenueLyricColours.FromHex(notHex));
}
