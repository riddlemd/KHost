using System.Reflection;
using System.Text.RegularExpressions;
using KHost.Screen2;

namespace KHost.UnitTests.Screen2;

/// <summary>
/// Rules the page keeps that nothing else can check. The screen's markup is embedded in the
/// executable and never runs in this suite, so a stacking order that reverses does so silently —
/// and is only ever seen by someone standing in the room with a phone.
/// </summary>
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

    /// <summary>
    /// A band pinned to an edge crosses both corners on that edge. A code it covers stops scanning
    /// with nothing on screen to say why, where a band with a code over one end of it is still a
    /// readable band — so the corner wins, and a guest can always reach the code.
    /// </summary>
    [Fact]
    public void ACornerSitsAboveTheMarquee()
        => Assert.True(ZIndexOf(".kh-corner") > ZIndexOf("#marquee"),
            "A QR code covered by the marquee cannot be scanned, and nothing on screen says so.");

    /// <summary>
    /// Both corner items share one stacking context, so the band cannot slice between a card and
    /// the code under it when a venue puts them in the same corner.
    /// </summary>
    [Fact]
    public void TheCornerIsTheOneThatCarriesTheStackingOrder()
    {
        Assert.DoesNotMatch(new Regex(@"\.kh-qr\s*\{[^}]*?z-index", RegexOptions.Singleline), Page);
        Assert.DoesNotMatch(new Regex(@"\.kh-break-music\s*\{[^}]*?z-index", RegexOptions.Singleline), Page);
    }
}
