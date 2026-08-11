using KHost.Domain.Services.QueueRotation.Modes;

namespace KHost.UnitTests.Domain.Services.QueueRotation.Modes;

public class ShuffleBucketStrategyTests
{
    [Fact]
    public async Task LowerSongCounts_ComeFirst()
    {
        var twoSongs = QueueRotationTestHelpers.User("Two");
        var zeroSongs = QueueRotationTestHelpers.User("Zero");
        var oneSong = QueueRotationTestHelpers.User("One");
        var songs = new Dictionary<Guid, int>
        {
            [twoSongs.Id] = 2,
            [oneSong.Id] = 1,
        };

        var ctx = QueueRotationTestHelpers.Context([twoSongs, zeroSongs, oneSong], songsSung: songs);

        var result = await new ShuffleBucketStrategy().ApplyAsync(ctx);

        Assert.Equal(zeroSongs.Id, result[0]);
        Assert.Equal(oneSong.Id, result[1]);
        Assert.Equal(twoSongs.Id, result[2]);
    }
}
