using KHost.Abstractions.Models;
using KHost.Domain.Services.BurnIn;

namespace KHost.UnitTests.Domain.Services.BurnIn;

/// <summary>What the painter puts on a frame at a moment in the song, read back off the pixels it
/// painted: no ffmpeg and no process, which is the point of painting apart from encoding.</summary>
/// <remarks>The frame is the lyrics' own 640x360 space, so a timing unit is a pixel and every
/// position asserted here is the one the timing names.</remarks>
public class TimedLyricsPainterTests
{
    private const int Width = 640, Height = 360;

    private static readonly LyricColor Red = new(255, 0, 0);
    private static readonly LyricColor Green = new(0, 255, 0);
    private static readonly LyricColor Blue = new(0, 0, 255);
    private static readonly LyricColor White = new(255, 255, 255);

    [Fact]
    public void Paint_BeforeTheSyllableIsSung_PaintsItOnlyInTheInactiveColour()
    {
        var ink = Read(PaintAt(OneSyllable(), 0.75), IsRed, IsWhite);

        Assert.True(ink[1].Count > 0, "the page was not on screen ahead of its first syllable");
        Assert.Equal(0, ink[0].Count);
    }

    [Fact]
    public void Paint_OnceTheSyllableIsSung_PaintsItInTheActiveColour()
    {
        var ink = Read(PaintAt(OneSyllable(), 2.25), IsRed, IsWhite);

        Assert.True(ink[0].Count > ink[1].Count * 10, $"{ink[1].Count} white pixels against {ink[0].Count} red once sung");
    }

    /// <summary>Linear with the song: half way through, the sung colour stops short of where the
    /// word ends and the waiting colour carries on past it.</summary>
    [Fact]
    public void Paint_HalfWayThroughTheSyllable_WipesOnlyItsLeftPart()
    {
        var ink = Read(PaintAt(OneSyllable(), 1.5), IsRed, IsWhite);
        var (red, white) = (ink[0], ink[1]);

        Assert.True(red.Count > 0 && white.Count > 0, $"red {red.Count}, white {white.Count}: expected both half way");
        Assert.True(red.MaxX < white.MaxX, $"the sung colour reached x={red.MaxX}, past the waiting colour at x={white.MaxX}");

        // Half the word, give or take the letter edges: the wipe is a clip across the glyphs.
        var midpoint = (red.MinX + white.MaxX) / 2.0;
        Assert.InRange(red.MaxX, midpoint - 12, midpoint + 12);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(3.5)]
    public void Paint_OutsideThePagesWindow_PaintsNothing(double t)
        => Assert.Equal(0, Covered(PaintAt(OneSyllable(), t)));

    /// <summary>A timing that leaves the colours to the screen gets the screen's theme.</summary>
    [Fact]
    public void Paint_APageWithNoActiveColour_WipesInTheThemeColour()
    {
        var lyrics = OneSyllable(active: null);

        var ink = Read(PaintAt(lyrics, 2.25), IsThemeActive, IsRed);

        Assert.True(ink[0].Count > 100, "a sung syllable with no colour of its own was not in the theme colour");
        Assert.Equal(0, ink[1].Count);
    }

    /// <summary>The bar fills across its whole window: half way through the gap, half of it.</summary>
    [Fact]
    public void Paint_ACountIn_FillsInProportionToTheGap()
    {
        var ink = Read(PaintAt(CountIn(), 2.0), IsGreen, IsBlue);

        // The bar spans x 100..500, so half full ends at 300.
        Assert.InRange(ink[0].MaxX, 297, 303);
        Assert.InRange(ink[1].MaxX, 497, 500);
    }

    [Fact]
    public void Paint_ACountInBeforeItsLastSteps_ShowsNoCountdown()
        => Assert.Equal(0, Read(PaintAt(CountIn(), 0.9), IsWhite)[0].Count);

    [Fact]
    public void Paint_ACountInInItsLastSteps_CountsDown()
        => Assert.True(Read(PaintAt(CountIn(), 2.0), IsWhite)[0].Count > 50, "no countdown digit over the bar");

    /// <summary>Gone by the time the page shows, not after: a singer pre-reads the first line as it
    /// lands, often over the bar's own spot.</summary>
    [Fact]
    public void Paint_ACountIn_IsGoneByTheTimeTheNextPageArrives()
    {
        var lyrics = CountIn(pageArrivesAt: 3.0);

        Assert.True(Read(PaintAt(lyrics, 2.4), IsGreen)[0].Count > 0, "the bar left before the handover began");
        Assert.Equal(0, Covered(PaintAt(lyrics, 3.0)));
    }

    /// <summary>The block runs from where the lead-in sets off to the line's leading edge, arriving
    /// as the first syllable lights.</summary>
    [Theory]
    [InlineData(1.0, 40)]
    [InlineData(2.0, 120)]
    [InlineData(2.99, 199.2)]
    public void Paint_ALeadIn_TravelsToTheLinesLeadingEdge(double t, double expectedCentre)
        => Assert.InRange(Read(PaintAt(LeadIn(rightToLeft: false), t), IsGreen)[0].MeanX, expectedCentre - 2, expectedCentre + 2);

    [Fact]
    public void Paint_ALeadIn_IsGoneOnceTheLineStarts()
        => Assert.Equal(0, Read(PaintAt(LeadIn(rightToLeft: false), 3.0), IsGreen)[0].Count);

    /// <summary>Right to left, the same run is made into the line's right edge from outside it.</summary>
    [Fact]
    public void Paint_ALeadInRightToLeft_TravelsInToTheRightEdge()
        => Assert.InRange(Read(PaintAt(LeadIn(rightToLeft: true), 2.0), IsGreen)[0].MeanX, 578.5, 581.5);

    /// <summary>Right to left the first syllable sits rightmost and each wipes in from its right.</summary>
    [Fact]
    public void Paint_RightToLeft_MirrorsTheOrderAndTheWipe()
    {
        var rtl = Read(PaintAt(TwoSyllables(rightToLeft: true), 2.5), IsRed, IsWhite);
        var ltr = Read(PaintAt(TwoSyllables(rightToLeft: false), 2.5), IsRed, IsWhite);

        // First syllable sung and the second half way: the only white left is at the far end.
        Assert.True(rtl[1].MinX < rtl[0].MinX && rtl[1].MaxX < rtl[0].MaxX, $"right to left: white {rtl[1].MinX}..{rtl[1].MaxX}, red {rtl[0].MinX}..{rtl[0].MaxX}");
        Assert.True(ltr[0].MinX < ltr[1].MinX && ltr[0].MaxX < ltr[1].MaxX, $"left to right: white {ltr[1].MinX}..{ltr[1].MaxX}, red {ltr[0].MinX}..{ltr[0].MaxX}");

        // Laid from the box's right edge, where the lead-in arrives.
        Assert.InRange(rtl[0].MaxX, 585, 604);
    }

    [Fact]
    public void LineBoxes_StackALineWithNoPositionUnderTheOneBefore()
    {
        var boxes = TimedLyricsPainter.LineBoxes(
        [
            new LyricLine { Position = new LyricBox(10, 50, 300, 30) },
            new LyricLine(),
            new LyricLine(),
        ]);

        Assert.Equal(80, boxes[1].Y);
        Assert.Equal(boxes[1].Y + boxes[1].Height, boxes[2].Y);
    }

    /// <summary>Words over a picture nobody made with them in mind: the band they sit in is darkened
    /// and the picture above it left alone.</summary>
    [Fact]
    public void Paint_OverAPicture_DarkensOnlyTheBandTheWordsSitIn()
    {
        var painter = new TimedLyricsPainter(OneSyllable(), Width, Height, scrim: true);
        using var worker = painter.CreateWorker();
        using var frame = painter.CreateFrame();
        worker.Paint(frame, 0);

        static int AlphaAt(byte[] pixels, int y) => pixels[(y * Width + 4) * 4 + 3];

        Assert.Equal(0, AlphaAt(frame.Pixels, Height / 2));
        Assert.True(AlphaAt(frame.Pixels, Height - 1) > 150, "the band under the words was left clear");
    }

    [Fact]
    public void Paint_OverBlack_LeavesTheFrameClear()
        => Assert.Equal(0, PaintAt(OneSyllable(), 0)[((Height - 1) * Width + 4) * 4 + 3]);

    [Fact]
    public void Frames_CarryStraightAlpha_BecauseFfmpegReadsRgbaAsStraight()
        => Assert.Equal(SkiaSharp.SKAlphaType.Unpremul, TimedLyricsPainter.FrameAlphaType);

    private static byte[] PaintAt(TimedLyrics lyrics, double t)
    {
        var painter = new TimedLyricsPainter(lyrics, Width, Height);
        using var worker = painter.CreateWorker();
        using var frame = painter.CreateFrame();
        worker.Paint(frame, t);
        return [.. frame.Pixels];
    }

    /// <summary>How many pixels carry anything at all.</summary>
    private static int Covered(byte[] pixels) => Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4 + 3] != 0);

    private sealed record Ink(int Count, int MinX, int MaxX, double MeanX);

    private static Ink[] Read(byte[] pixels, params Func<byte, byte, byte, byte, bool>[] matches)
    {
        var counts = new int[matches.Length];
        var min = Enumerable.Repeat(int.MaxValue, matches.Length).ToArray();
        var max = Enumerable.Repeat(-1, matches.Length).ToArray();
        var sum = new double[matches.Length];

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var x = i / 4 % Width;
            for (var m = 0; m < matches.Length; m++)
            {
                if (!matches[m](pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3])) continue;
                counts[m]++;
                min[m] = Math.Min(min[m], x);
                max[m] = Math.Max(max[m], x);
                sum[m] += x;
            }
        }

        return [.. Enumerable.Range(0, matches.Length).Select(m => new Ink(counts[m], min[m], max[m], counts[m] == 0 ? -1 : sum[m] / counts[m]))];
    }

    private static bool IsRed(byte r, byte g, byte b, byte a) => a > 200 && r > 200 && g < 60 && b < 60;
    private static bool IsGreen(byte r, byte g, byte b, byte a) => a > 200 && g > 200 && r < 60 && b < 60;
    private static bool IsBlue(byte r, byte g, byte b, byte a) => a > 200 && b > 200 && r < 60 && g < 60;
    private static bool IsWhite(byte r, byte g, byte b, byte a) => a > 200 && r > 200 && g > 200 && b > 200;

    private static bool IsThemeActive(byte r, byte g, byte b, byte a)
        => a > 200 && Math.Abs(r - 0x85) < 8 && Math.Abs(g - 0x58) < 8 && Math.Abs(b - 0xFA) < 8;

    private static TimedLyrics Song(IReadOnlyList<LyricPage> pages, bool rightToLeft = false, IReadOnlyList<LyricCountIn>? countIns = null) => new()
    {
        DurationSeconds = 5,
        Bounds = new LyricBox(0, 0, Width, Height),
        IsRightToLeft = rightToLeft,
        Pages = pages,
        CountIns = countIns ?? [],
    };

    /// <summary>One page, one syllable sung from 1s to 2s. Wide glyphs so the wipe covers enough
    /// pixels to measure.</summary>
    private static TimedLyrics OneSyllable(LyricColor? active) => Song(
    [
        new LyricPage
        {
            ShowFromSeconds = 0.5,
            ShowUntilSeconds = 3,
            Active = active,
            Inactive = White,
            Lines = [new LyricLine { Position = new LyricBox(40, 120, 400, 120), Syllables = [new(1, 2, "WWWW")] }],
        },
    ]);

    private static TimedLyrics OneSyllable() => OneSyllable(Red);

    private static TimedLyrics TwoSyllables(bool rightToLeft) => Song(
    [
        new LyricPage
        {
            ShowFromSeconds = 0,
            ShowUntilSeconds = 4,
            Active = Red,
            Inactive = White,
            Lines = [new LyricLine { Position = new LyricBox(40, 120, 560, 100), Syllables = [new(1, 2, "HHH"), new(2, 3, "HHH")] }],
        },
    ], rightToLeft);

    /// <summary>A bar across 100..500 from 0s to 4s, counting down its last three one-second steps.</summary>
    private static TimedLyrics CountIn(double? pageArrivesAt = null) => Song(
        pageArrivesAt is { } at ? [new LyricPage { ShowFromSeconds = at, ShowUntilSeconds = at + 5 }] : [],
        countIns:
        [
            new LyricCountIn
            {
                StartSeconds = 0,
                EndSeconds = 4,
                Position = new LyricBox(100, 200, 400, 20),
                StepSeconds = 1,
                Steps = 3,
                Active = Green,
                Inactive = Blue,
            },
        ]);

    /// <summary>A line at 200..500 whose first syllable lights at 3s, led in from x=40 from 1s.</summary>
    private static TimedLyrics LeadIn(bool rightToLeft) => Song(
    [
        new LyricPage
        {
            ShowFromSeconds = 0,
            ShowUntilSeconds = 5,
            Active = Green,
            Inactive = White,
            Lines =
            [
                new LyricLine
                {
                    Position = new LyricBox(200, 100, 300, 60),
                    Syllables = [new(3, 4, "HHH")],
                    LeadIn = new LyricLeadIn(1, 40),
                },
            ],
        },
    ], rightToLeft);
}
