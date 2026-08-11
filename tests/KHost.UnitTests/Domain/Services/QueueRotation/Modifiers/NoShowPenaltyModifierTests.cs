using KHost.Plugins.Sdk.Models.QueueRotation;
using KHost.Domain.Services.QueueRotation.Modifiers;

namespace KHost.UnitTests.Domain.Services.QueueRotation.Modifiers;

public class NoShowPenaltyModifierTests
{
    [Fact]
    public async Task SingerWithMisses_DemotedBySlots()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var carol = QueueRotationTestHelpers.User("Carol");
        var dave = QueueRotationTestHelpers.User("Dave");
        var missed = new Dictionary<Guid, int> { [alice.Id] = 1 };
        var config = new QueueRotationConfig { NoShowDemoteSlots = 2, NoShowMaxMisses = 3 };
        var ctx = QueueRotationTestHelpers.Context([alice, bob, carol, dave], config: config, missedCalls: missed);

        var result = await new NoShowPenaltyModifier(new IdentityInnerStrategy()).ApplyAsync(ctx);

        Assert.Equal(new[] { bob.Id, carol.Id, alice.Id, dave.Id }, result);
    }

    [Fact]
    public async Task NoMisses_PassesThrough()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var config = new QueueRotationConfig { NoShowDemoteSlots = 2 };
        var ctx = QueueRotationTestHelpers.Context([alice, bob], config: config);

        var result = await new NoShowPenaltyModifier(new IdentityInnerStrategy()).ApplyAsync(ctx);

        Assert.Equal(new[] { alice.Id, bob.Id }, result);
    }
}
