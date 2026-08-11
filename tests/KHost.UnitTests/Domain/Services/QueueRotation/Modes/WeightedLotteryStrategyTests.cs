using KHost.Domain.Services.QueueRotation.Modes;

namespace KHost.UnitTests.Domain.Services.QueueRotation.Modes;

public class WeightedLotteryStrategyTests
{
    [Fact]
    public async Task FinishedSinger_AlwaysGoesToEnd_AndAllSingersIncluded()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var carol = QueueRotationTestHelpers.User("Carol");
        var ctx = QueueRotationTestHelpers.Context([alice, bob, carol], finishedSingerId: alice.Id);

        var result = await new WeightedLotteryStrategy().ApplyAsync(ctx);

        Assert.Equal(alice.Id, result[^1]);
        Assert.Equal(3, result.Count);
        Assert.Equal(new HashSet<Guid> { alice.Id, bob.Id, carol.Id }, result.ToHashSet());
    }
}
