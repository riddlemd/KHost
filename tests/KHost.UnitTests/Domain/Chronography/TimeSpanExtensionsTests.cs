using KHost.Domain.Chronography;

namespace KHost.UnitTests.Domain.Chronography;

/// <summary>
/// Every song duration in the library and the queue is printed through this, so the format is a
/// visible contract rather than a detail.
/// </summary>
public class TimeSpanExtensionsTests
{
    [Theory]
    [InlineData(0, 0, "00:00")]
    [InlineData(0, 7, "00:07")]
    [InlineData(4, 30, "04:30")]
    [InlineData(9, 59, "09:59")]
    public void ToTotalMinutesAndSeconds_PadsBothHalves(int minutes, int seconds, string expected)
        => Assert.Equal(expected, new TimeSpan(0, minutes, seconds).ToTotalMinutesAndSeconds());

    [Fact]
    public void ToTotalMinutesAndSeconds_CountsMinutesPastAnHour_RatherThanRollingOver()
    {
        // Total minutes, not the minutes component: a 90 minute recording is 90:00, not 30:00.
        Assert.Equal("90:00", TimeSpan.FromMinutes(90).ToTotalMinutesAndSeconds());
        Assert.Equal("61:05", new TimeSpan(1, 1, 5).ToTotalMinutesAndSeconds());
    }

    [Fact]
    public void ToTotalMinutesAndSeconds_TruncatesTowardsZero_RatherThanRoundingUp()
    {
        // 119.9s is still 01:59; rounding would show a song as a second longer than it plays.
        Assert.Equal("01:59", TimeSpan.FromSeconds(119.9).ToTotalMinutesAndSeconds());
    }

    [Fact]
    public void ToTotalMinutesAndSeconds_PutsMinutesFirst()
    {
        // The halves are both zero-padded two-digit numbers, so a swap is invisible without this.
        Assert.Equal("05:01", new TimeSpan(0, 5, 1).ToTotalMinutesAndSeconds());
    }
}
