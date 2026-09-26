using KHost.Abstractions.Models;

namespace KHost.UnitTests.Abstractions.Models;

// A provider that states no gaps must still hand a screen something it can walk without a null check.
public class TimedLyricsTests
{
    [Fact]
    public void CountIns_WhenNoneAreGiven_IsEmpty()
        => Assert.Empty(new TimedLyrics { DurationSeconds = 1, Bounds = new LyricBox(0, 0, 640, 360) }.CountIns);

    [Fact]
    public void LeadIn_WhenNoneIsGiven_IsNull()
        => Assert.Null(new LyricLine().LeadIn);

    [Fact]
    public void CountIn_WhenOnlyItsWindowAndPlaceAreGiven_HasNoCountdownNoColoursAndNoOutline()
    {
        var countIn = new LyricCountIn { StartSeconds = 1, EndSeconds = 20, Position = new LyricBox(60, 282.5, 520, 35) };

        Assert.Equal((0d, 0), (countIn.StepSeconds, countIn.Steps));
        Assert.Null(countIn.Active);
        Assert.Null(countIn.Inactive);
        Assert.Null(countIn.Border);
        Assert.Equal(0, countIn.BorderWidth);
    }
}
