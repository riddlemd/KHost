using KHost.Abstractions.Models;
using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

public class LyricsOffsetTests
{
    private static TimedLyrics Song() => new()
    {
        DurationSeconds = 200,
        Bounds = new LyricBox(0, 0, 1280, 720),
        Pages =
        [
            new LyricPage
            {
                ShowFromSeconds = 10,
                ShowUntilSeconds = 20,
                Lines =
                [
                    new LyricLine
                    {
                        Syllables = [new LyricSyllable(12, 13, "la"), new LyricSyllable(13, 14, "la")],
                        LeadIn = new LyricLeadIn(11, 50),
                    },
                    new LyricLine { Syllables = [new LyricSyllable(15, 16, "di")] },
                ],
            },
        ],
        CountIns = [new LyricCountIn { StartSeconds = 5, EndSeconds = 10, Position = new LyricBox(0, 0, 10, 10) }],
    };

    /// <summary>Every moment the drawers read moves together, or the chase tears away from its page.</summary>
    [Fact]
    public void Shift_MovesEveryMomentByTheOffset()
    {
        var shifted = LyricsOffset.Shift(Song(), 0.25);

        var page = shifted.Pages[0];
        Assert.Equal((10.25, 20.25), (page.ShowFromSeconds, page.ShowUntilSeconds));
        Assert.Equal([(12.25, 13.25), (13.25, 14.25)], page.Lines[0].Syllables.Select(s => (s.StartSeconds, s.EndSeconds)));
        Assert.Equal(11.25, page.Lines[0].LeadIn!.StartSeconds);
        Assert.Equal((15.25, 16.25), (page.Lines[1].Syllables[0].StartSeconds, page.Lines[1].Syllables[0].EndSeconds));
        Assert.Equal((5.25, 10.25), (shifted.CountIns[0].StartSeconds, shifted.CountIns[0].EndSeconds));
    }

    [Fact]
    public void Shift_Negative_MovesEveryMomentEarlier()
    {
        var shifted = LyricsOffset.Shift(Song(), -0.5);

        Assert.Equal(9.5, shifted.Pages[0].ShowFromSeconds);
        Assert.Equal(11.5, shifted.Pages[0].Lines[0].Syllables[0].StartSeconds);
        Assert.Equal(10.5, shifted.Pages[0].Lines[0].LeadIn!.StartSeconds);
        Assert.Equal(4.5, shifted.CountIns[0].StartSeconds);
    }

    /// <summary>The length is the timing's own statement of the song, not a moment in it.</summary>
    [Fact]
    public void Shift_LeavesTheLengthAndTheWordsAlone()
    {
        var shifted = LyricsOffset.Shift(Song(), 1);

        Assert.Equal(200, shifted.DurationSeconds);
        Assert.Equal(["la", "la"], shifted.Pages[0].Lines[0].Syllables.Select(s => s.Text));
        Assert.Null(shifted.Pages[0].Lines[1].LeadIn);
    }

    [Fact]
    public void Shift_Zero_ReturnsTheSameTiming()
    {
        var song = Song();

        Assert.Same(song, LyricsOffset.Shift(song, 0));
    }

    [Theory]
    [InlineData(5000, 2000)]
    [InlineData(-5000, -2000)]
    [InlineData(2000, 2000)]
    [InlineData(-120, -120)]
    public void ClampMilliseconds_HoldsTheOffsetWithinTheMaximum(int typed, int expected)
    {
        Assert.Equal(expected, LyricsOffset.ClampMilliseconds(typed));
    }
}
