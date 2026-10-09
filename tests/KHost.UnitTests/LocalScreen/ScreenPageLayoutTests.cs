using System.Reflection;
using System.Text.RegularExpressions;
using KHost.LocalScreen;

namespace KHost.UnitTests.LocalScreen;

/// <summary>The embedded markup never runs here, so a reversed stacking order fails silently.</summary>
public class ScreenPageLayoutTests
{
    private static readonly string Page = ReadEmbeddedPage();

    private static string ReadEmbeddedPage()
    {
        var assembly = typeof(StreamMediaPlayer).Assembly;

        using var stream = assembly.GetManifestResourceStream("screen-ui/index.html")
            ?? throw new InvalidOperationException(
                "The screen page is not embedded under the name this test reads it by. Names come "
                + $"from LogicalName in the csproj; found: {string.Join(", ", assembly.GetManifestResourceNames())}");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    /// <summary>The venue dialog shows these as what an unset marquee singer or song is drawn in.</summary>
    [Theory]
    [InlineData(".marquee-singer", "--marquee-singer-fg", KHost.UserInterface.Models.ScreenColourDefaults.MarqueeSinger)]
    [InlineData(".marquee-song", "--marquee-song-fg", KHost.UserInterface.Models.ScreenColourDefaults.MarqueeSong)]
    public void AMarqueeNameLeftUnset_IsTheColourTheDialogShows(string selector, string variable, string colour)
        => Assert.Matches(new Regex(Regex.Escape(selector) + @"\s*\{\s*color:\s*var\(" + Regex.Escape(variable) + @",\s*" + Regex.Escape(colour) + @"\)"), Page);

    /// <summary>The number that decides which of two overlapping things the room actually sees.</summary>
    private static int ZIndexOf(string selector)
    {
        var match = Regex.Match(Page, Regex.Escape(selector) + @"\s*\{[^}]*?z-index:\s*(\d+)", RegexOptions.Singleline);

        Assert.True(match.Success, $"No z-index found on '{selector}'.");

        return int.Parse(match.Groups[1].Value);
    }

    /// <summary>A band covering a code stops it scanning with no sign why, so the corner must win.</summary>
    [Fact]
    public void ACornerSitsAboveTheMarquee()
        => Assert.True(ZIndexOf(".kh-corner") > ZIndexOf("#marquee"),
            "A QR code covered by the marquee cannot be scanned, and nothing on screen says so.");

    /// <summary>Both corner items share one stacking context; the band cannot slice between them.</summary>
    [Fact]
    public void TheCornerIsTheOneThatCarriesTheStackingOrder()
    {
        Assert.DoesNotMatch(new Regex(@"\.kh-qr\s*\{[^}]*?z-index", RegexOptions.Singleline), Page);
        Assert.DoesNotMatch(new Regex(@"\.kh-break-music\s*\{[^}]*?z-index", RegexOptions.Singleline), Page);
    }

    /// <summary>Full screen is how a room runs; a window's bar left over the picture is in every song.</summary>
    [Fact]
    public void TheTitleBarAndEdgesHideInFullScreen()
    {
        Assert.Matches(new Regex(@":root\[data-fullscreen=""true""\] \.kh-titlebar\s*,[^{]*:root\[data-fullscreen=""true""\] \.kh-resize-edge[^{]*\{\s*display:\s*none;", RegexOptions.Singleline), Page);
    }

    /// <summary>The window's controls float over the picture; a stage that gave them a row of its own
    /// would letterbox every song into a shorter window than the one the room sees.</summary>
    [Fact]
    public void TheStageFillsTheWholeWindow()
    {
        Assert.Matches(new Regex(@"#stage\s*\{[^}]*position:\s*absolute;[^}]*inset:\s*0;", RegexOptions.Singleline), Page);
        Assert.Matches(new Regex(@"\.kh-titlebar\s*\{[^}]*position:\s*fixed;", RegexOptions.Singleline), Page);
        Assert.DoesNotMatch(new Regex(@"body\s*\{[^}]*display:\s*flex", RegexOptions.Singleline), Page);
    }

    /// <summary>The strip is grabbed, not seen: a background or a title would sit over every song.</summary>
    [Fact]
    public void TheDragStripIsInvisible()
    {
        Assert.DoesNotMatch(new Regex(@"\.kh-titlebar\s*\{[^}]*background", RegexOptions.Singleline), Page);
        Assert.DoesNotContain("kh-titlebar__title", Page, StringComparison.Ordinal);
    }

    /// <summary>White alone vanishes over a bright picture and black over a dark one; both are needed.</summary>
    [Fact]
    public void TheButtonGlyphsAreWhiteWithABlackOutline()
    {
        Assert.Matches(new Regex(@"\.kh-titlebar__button\s*\{[^}]*color:\s*#fff;", RegexOptions.Singleline), Page);
        Assert.Matches(new Regex(@"\.kh-titlebar__button svg\s*\{[^}]*stroke:\s*currentColor;[^}]*filter:\s*drop-shadow\([^)]*#000\)", RegexOptions.Singleline), Page);
    }

    /// <summary>A resting pointer fades the buttons; one on them must still be able to press them.</summary>
    [Fact]
    public void IdleButtonsFadeButNotWhileHovered()
    {
        Assert.Matches(new Regex(@":root\[data-pointer-idle=""true""\] \.kh-titlebar__buttons\s*\{\s*opacity:\s*0;", RegexOptions.Singleline), Page);
        Assert.Matches(new Regex(@":root\[data-pointer-idle=""true""\] \.kh-titlebar__buttons:hover\s*\{\s*opacity:\s*1;", RegexOptions.Singleline), Page);
    }

    /// <summary>A maximised window is the picture alone; double-clicking the strip is the way out, so
    /// the strip itself must stay.</summary>
    [Fact]
    public void MaximisedHidesTheButtonsButKeepsTheStrip()
    {
        Assert.Matches(new Regex(@":root\[data-maximised=""true""\] \.kh-titlebar__buttons\s*,[^{]*\{\s*display:\s*none;", RegexOptions.Singleline), Page);
        Assert.DoesNotMatch(new Regex(@":root\[data-maximised=""true""\] \.kh-titlebar\s*[,{]", RegexOptions.Singleline), Page);
    }

    /// <summary>The strip sits over the picture's top edge, so it has to be the one that wins there.</summary>
    [Fact]
    public void TheDragStripSitsAboveEverythingOnTheStage()
        => Assert.True(ZIndexOf(".kh-titlebar") > ZIndexOf(".kh-corner"),
            "Under a corner, the strip would stop moving the window wherever a code sat.");
}
