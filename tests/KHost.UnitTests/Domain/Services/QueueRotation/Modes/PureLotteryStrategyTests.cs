using KHost.Domain.Services.QueueRotation.Modes;

namespace KHost.UnitTests.Domain.Services.QueueRotation.Modes;

public class PureLotteryStrategyTests
{
    [Fact]
    public async Task FinishedSinger_AlwaysGoesToEnd()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var carol = QueueRotationTestHelpers.User("Carol");
        var dave = QueueRotationTestHelpers.User("Dave");
        var ctx = QueueRotationTestHelpers.Context([alice, bob, carol, dave], finishedSingerId: alice.Id);

        var result = await new PureLotteryStrategy().ApplyAsync(ctx);

        Assert.Equal(alice.Id, result[^1]);
        Assert.Equal(4, result.Count);
        Assert.Equal(new HashSet<Guid> { alice.Id, bob.Id, carol.Id, dave.Id }, result.ToHashSet());
    }
}
