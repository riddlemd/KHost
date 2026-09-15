using KHost.Abstractions.Models;
using KHost.UserInterface.Models;

namespace KHost.UnitTests.UserInterface.Models;

public class MediaStatusDisplayTests
{
    [Fact]
    public void BadgeClass_Processing_MatchesDownloading()
    {
        // Two phases of one acquisition: a colour of its own would read as a different kind of
        // state, and the word in the badge is already what says which phase.
        Assert.Equal(MediaStatusDisplay.BadgeClass(MediaStatus.Downloading), MediaStatusDisplay.BadgeClass(MediaStatus.Processing));
    }

    [Fact]
    public void BadgeClass_Processing_IsNotTheUnknownFallback()
    {
        Assert.NotEqual(MediaStatusDisplay.BadgeClass(MediaStatus.Unknown), MediaStatusDisplay.BadgeClass(MediaStatus.Processing));
    }

    [Theory]
    [InlineData(MediaStatus.Ready)]
    [InlineData(MediaStatus.Broken)]
    public void IsUserSettable_SettledStatus_IsTrue(MediaStatus status)
        => Assert.True(MediaStatusDisplay.IsUserSettable(status));

    [Theory]
    [InlineData(MediaStatus.Downloading)]
    [InlineData(MediaStatus.Processing)]
    [InlineData(MediaStatus.Unknown)]
    public void IsUserSettable_InFlightOrUnestablishedStatus_IsFalse(MediaStatus status)
        => Assert.False(MediaStatusDisplay.IsUserSettable(status));
}
