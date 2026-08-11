using KHost.Domain.Services.QueueRotation.Modes;

namespace KHost.UnitTests.Domain.Services.QueueRotation.Modes;

public class LongestWaitFirstStrategyTests
{
    [Fact]
    public async Task OrdersByLastSangOn_NeverSungFirst()
    {
        var now = new DateTime(2026, 4, 28, 20, 0, 0, DateTimeKind.Utc);
        var newcomer = QueueRotationTestHelpers.User("Newcomer", lastSangOn: null);
        var sangAnHourAgo = QueueRotationTestHelpers.User("Hour", lastSangOn: now.AddHours(-1));
        var sangFiveMinAgo = QueueRotationTestHelpers.User("Five", lastSangOn: now.AddMinutes(-5));
        var ctx = QueueRotationTestHelpers.Context([sangFiveMinAgo, newcomer, sangAnHourAgo], now: now);

        var result = await new LongestWaitFirstStrategy().ApplyAsync(ctx);

        Assert.Equal(new[] { newcomer.Id, sangAnHourAgo.Id, sangFiveMinAgo.Id }, result);
    }
}
