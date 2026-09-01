using KHost.Abstractions.Models.QueueRotation;
using KHost.Domain.Services.QueueRotation.Modes;

namespace KHost.UnitTests.Domain.Services.QueueRotation.Modes;

public class RoundRobinStrategyTests
{
    [Fact]
    public async Task FinishedSinger_AlwaysGoesToEnd_IgnoringDropConfig()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var carol = QueueRotationTestHelpers.User("Carol");
        var config = new QueueRotationConfig { DropPosition = DropPositionMode.FixedIndex, DropFixedIndex = 1 };
        var ctx = QueueRotationTestHelpers.Context([alice, bob, carol], finishedSingerId: alice.Id, config: config);

        var result = await new RoundRobinStrategy().ApplyAsync(ctx);

        Assert.Equal(new[] { bob.Id, carol.Id, alice.Id }, result);
    }
}
