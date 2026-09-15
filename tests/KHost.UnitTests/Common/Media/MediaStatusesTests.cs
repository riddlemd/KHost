using KHost.Abstractions.Models;
using KHost.Common.Media;

namespace KHost.UnitTests.Common.Media;

public class MediaStatusesTests
{
    [Theory]
    [InlineData(MediaStatus.Downloading)]
    [InlineData(MediaStatus.Processing)]
    public void IsAcquiring_InFlightStatus_IsTrue(MediaStatus status)
        => Assert.True(status.IsAcquiring());

    [Theory]
    [InlineData(MediaStatus.Ready)]
    [InlineData(MediaStatus.Broken)]
    [InlineData(MediaStatus.Unknown)]
    public void IsAcquiring_SettledOrUnestablishedStatus_IsFalse(MediaStatus status)
        => Assert.False(status.IsAcquiring());

    [Fact]
    public void Acquiring_HoldsExactlyTheStatusesIsAcquiringAccepts()
    {
        // The set feeds EF where the method cannot go, so the two drifting apart would let a query
        // and an in-memory check disagree about the same row.
        var expected = Enum.GetValues<MediaStatus>().Where(status => status.IsAcquiring());

        Assert.Equal(expected, MediaStatuses.Acquiring);
    }
}
