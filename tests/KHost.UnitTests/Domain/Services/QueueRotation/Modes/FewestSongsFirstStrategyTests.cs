using KHost.Domain.Services.QueueRotation.Modes;

namespace KHost.UnitTests.Domain.Services.QueueRotation.Modes;

public class FewestSongsFirstStrategyTests
{
    [Fact]
    public async Task OrdersBySongCount_TiesBrokenByOldestSang()
    {
        var now = new DateTime(2026, 4, 28, 20, 0, 0, DateTimeKind.Utc);
        var threeSongs = QueueRotationTestHelpers.User("Three", lastSangOn: now.AddMinutes(-1));
        var oneSongOld = QueueRotationTestHelpers.User("OneOld", lastSangOn: now.AddHours(-2));
        var oneSongRecent = QueueRotationTestHelpers.User("OneRecent", lastSangOn: now.AddMinutes(-10));
        var newcomer = QueueRotationTestHelpers.User("Zero");
        var songs = new Dictionary<Guid, int>
        {
            [threeSongs.Id] = 3,
            [oneSongOld.Id] = 1,
            [oneSongRecent.Id] = 1,
        };

        var ctx = QueueRotationTestHelpers.Context([threeSongs, oneSongRecent, oneSongOld, newcomer], songsSung: songs, now: now);

        var result = await new FewestSongsFirstStrategy().ApplyAsync(ctx);

        Assert.Equal(new[] { newcomer.Id, oneSongOld.Id, oneSongRecent.Id, threeSongs.Id }, result);
    }
}
