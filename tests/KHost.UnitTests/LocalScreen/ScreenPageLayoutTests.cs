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

    /// <summary>The bar sits above the picture rather than over it, so nothing the room sees is under it.</summary>
    [Fact]
    public void TheTitleBarIsOutsideTheStage()
    {
        var bar = Page.IndexOf("id=\"titlebar\"", StringComparison.Ordinal);
        var stage = Page.IndexOf("<div id=\"stage\">", StringComparison.Ordinal);
        var video = Page.IndexOf("<video id=\"video\"", StringComparison.Ordinal);

        Assert.True(bar >= 0 && stage > bar && video > stage, "The bar must come before the stage, and the picture inside it.");
    }
}
