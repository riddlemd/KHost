using KHost.Domain.Services.QueueRotation.Modes;

namespace KHost.UnitTests.Domain.Services.QueueRotation.Modes;

public class WeightedFairStrategyTests
{
    [Fact]
    public async Task LongWaitWithFewSongs_ScoresHighest()
    {
        var now = new DateTime(2026, 4, 28, 20, 0, 0, DateTimeKind.Utc);
        var longWaitFewSongs = QueueRotationTestHelpers.User("LongWait", lastSangOn: now.AddHours(-2));
        var shortWaitManySongs = QueueRotationTestHelpers.User("ShortWait", lastSangOn: now.AddMinutes(-1));
        var midline = QueueRotationTestHelpers.User("Mid", lastSangOn: now.AddMinutes(-30));
        var songs = new Dictionary<Guid, int>
        {
            [longWaitFewSongs.Id] = 1,
            [shortWaitManySongs.Id] = 5,
            [midline.Id] = 2,
        };

        var ctx = QueueRotationTestHelpers.Context([shortWaitManySongs, midline, longWaitFewSongs], songsSung: songs, now: now);

        var result = await new WeightedFairStrategy().ApplyAsync(ctx);

        Assert.Equal(longWaitFewSongs.Id, result[0]);
        Assert.Equal(shortWaitManySongs.Id, result[^1]);
    }
}
