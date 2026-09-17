using System.Reflection;
using System.Text.RegularExpressions;
using KHost.Screen2;

namespace KHost.UnitTests.Screen2;

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
}
