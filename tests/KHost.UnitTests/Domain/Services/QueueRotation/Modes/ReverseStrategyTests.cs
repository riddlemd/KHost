using KHost.Domain.Services.QueueRotation.Modes;

namespace KHost.UnitTests.Domain.Services.QueueRotation.Modes;

public class ReverseStrategyTests
{
    [Fact]
    public async Task FinishedSinger_MovesToEnd()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var carol = QueueRotationTestHelpers.User("Carol");
        var ctx = QueueRotationTestHelpers.Context([alice, bob, carol], finishedSingerId: alice.Id);

        var result = await new ReverseStrategy().ApplyAsync(ctx);

        Assert.Equal(new[] { bob.Id, carol.Id, alice.Id }, result);
    }
}
