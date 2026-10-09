using KHost.Abstractions.Models;
using KHost.Common.Display;

namespace KHost.UnitTests.Common.Display;

public class ScreenColoursTests
{
    private const string Primary = "#111111", Highlight = "#222222", Text = "#333333", Shadow = "#444444";

    private static Venue.VenueSettings Themed() => new()
    {
        ThemePrimaryColor = Primary,
        ThemeHighlightColor = Highlight,
        ThemeTextColor = Text,
        ThemeShadowColor = Shadow,
    };

    /// <summary>Every colour left unset takes the theme colour its role names.</summary>
    [Fact]
    public void ResolveScreenColours_NothingOwn_TakesEachRolesThemeColour()
    {
        var colours = Themed().ResolveScreenColours();

        Assert.Equal<IEnumerable<string?>>(
            [Shadow, Highlight, Text, Shadow, Text, Shadow, Text, Highlight, Shadow, Text, Shadow, Text, Shadow, Shadow, Text, Primary, Highlight, Primary],
            [colours.Background, colours.LyricsSung, colours.LyricsUnsung, colours.LyricsOutline, colours.IntroText, colours.IntroOutline,
             colours.NextSingerText, colours.NextSingerName, colours.NextSingerPanel, colours.BreakMusicCardText, colours.BreakMusicCardBackground,
             colours.QrCodeFrame, colours.QrCodeCaption, colours.MarqueeBackground, colours.MarqueeText, colours.MarqueeSinger,
             colours.MarqueeSong, colours.MarqueeDivider]);
    }

    /// <summary>A venue's own colour wins over its theme, for every part the screen draws.</summary>
    [Fact]
    public void ResolveScreenColours_OwnColours_WinOverTheTheme()
    {
        var settings = Themed();
        settings.ScreenBackgroundColor = "#a00001";
        settings.LyricsSungColor = "#a00002";
        settings.LyricsUnsungColor = "#a00003";
        settings.LyricsOutlineColor = "#a00004";
        settings.IntroTextColor = "#a00005";
        settings.IntroOutlineColor = "#a00006";
        settings.NextSingerTextColor = "#a00007";
        settings.NextSingerNameColor = "#a00008";
        settings.NextSingerPanelColor = "#a00009";
        settings.BreakMusicCardTextColor = "#a0000a";
        settings.BreakMusicCardBackgroundColor = "#a0000b";
        settings.QrCodeFrameColor = "#a0000c";
        settings.QrCodeCaptionColor = "#a0000d";
        settings.MarqueeBackgroundColor = "#a0000e";
        settings.MarqueeTextColor = "#a0000f";
        settings.MarqueeSingerColor = "#a00010";
        settings.MarqueeSongColor = "#a00011";
        settings.MarqueeDividerColor = "#a00012";

        var colours = settings.ResolveScreenColours();

        Assert.Equal(
            Enumerable.Range(1, 18).Select(n => $"#a{n:x5}"),
            [colours.Background, colours.LyricsSung, colours.LyricsUnsung, colours.LyricsOutline, colours.IntroText, colours.IntroOutline,
             colours.NextSingerText, colours.NextSingerName, colours.NextSingerPanel, colours.BreakMusicCardText, colours.BreakMusicCardBackground,
             colours.QrCodeFrame, colours.QrCodeCaption, colours.MarqueeBackground, colours.MarqueeText, colours.MarqueeSinger,
             colours.MarqueeSong, colours.MarqueeDivider]);
    }

    /// <summary>No colour and no theme: every part is the screen's own, as before themes.</summary>
    [Fact]
    public void ResolveScreenColours_NoThemeAndNothingOwn_IsAllTheScreensOwn()
    {
        var colours = new Venue.VenueSettings().ResolveScreenColours();

        Assert.Equal(new ScreenColourSet(), colours);
    }

    /// <summary>Only <c>#rrggbb</c> counts: anything else is as if unset, and falls back.</summary>
    [Theory]
    [InlineData("red")]
    [InlineData("#fff")]
    [InlineData("  ")]
    [InlineData("#12345g")]
    public void ResolveScreenColours_AnOwnColourThatIsNotHex_FallsBackToTheTheme(string notHex)
    {
        var settings = Themed();
        settings.LyricsSungColor = notHex;

        Assert.Equal(Highlight, settings.ResolveScreenColours().LyricsSung);
    }

    [Fact]
    public void ResolveScreenColours_KeepsColoursAsLowercaseHex()
    {
        var settings = new Venue.VenueSettings { ThemeShadowColor = " #ABCDEF " };

        Assert.Equal("#abcdef", settings.ResolveScreenColours().Background);
    }

    [Fact]
    public void VisualisationPalette_IsPrimaryHighlightAndShadow()
        => Assert.Equal([Primary, Highlight, Shadow], Themed().ResolveScreenColours().VisualisationPalette);

    /// <summary>A partial theme still gives three colours, the missing ones taken from the main one.</summary>
    [Fact]
    public void VisualisationPalette_OnlyAPrimary_RepeatsIt()
        => Assert.Equal([Primary, Primary, Primary], new Venue.VenueSettings { ThemePrimaryColor = Primary }.ResolveScreenColours().VisualisationPalette);

    [Fact]
    public void VisualisationPalette_NoPrimary_TakesTheHighlightAsItsMain()
        => Assert.Equal([Highlight, Highlight, Shadow],
            new Venue.VenueSettings { ThemeHighlightColor = Highlight, ThemeShadowColor = Shadow }.ResolveScreenColours().VisualisationPalette);

    /// <summary>Text alone is no palette: a visualisation at such a venue keeps its own colours.</summary>
    [Fact]
    public void VisualisationPalette_NoPrimaryHighlightOrShadow_IsNone()
        => Assert.Null(new Venue.VenueSettings { ThemeTextColor = Text }.ResolveScreenColours().VisualisationPalette);
}
