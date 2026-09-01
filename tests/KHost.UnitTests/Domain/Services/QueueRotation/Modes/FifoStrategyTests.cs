using KHost.Abstractions.Models.QueueRotation;
using KHost.Domain.Services.QueueRotation.Modes;

namespace KHost.UnitTests.Domain.Services.QueueRotation.Modes;

public class FifoStrategyTests
{
    [Fact]
    public async Task FinishedSinger_MovesToEnd_ByDefault()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var carol = QueueRotationTestHelpers.User("Carol");
        var ctx = QueueRotationTestHelpers.Context([alice, bob, carol], finishedSingerId: alice.Id);

        var result = await new FifoStrategy().ApplyAsync(ctx);

        Assert.Equal(new[] { bob.Id, carol.Id, alice.Id }, result);
    }

    [Fact]
    public async Task FinishedSinger_HonorsFixedIndex()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var carol = QueueRotationTestHelpers.User("Carol");
        var dave = QueueRotationTestHelpers.User("Dave");
        var config = new QueueRotationConfig { DropPosition = DropPositionMode.FixedIndex, DropFixedIndex = 1 };
        var ctx = QueueRotationTestHelpers.Context([alice, bob, carol, dave], finishedSingerId: alice.Id, config: config);

        var result = await new FifoStrategy().ApplyAsync(ctx);

        Assert.Equal(new[] { bob.Id, alice.Id, carol.Id, dave.Id }, result);
    }

    [Fact]
    public async Task NoFinishedSinger_KeepsOrder()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var ctx = QueueRotationTestHelpers.Context([alice, bob]);

        var result = await new FifoStrategy().ApplyAsync(ctx);

        Assert.Equal(new[] { alice.Id, bob.Id }, result);
    }
}
