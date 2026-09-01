using KHost.Abstractions.Models.QueueRotation;
using KHost.Domain.Services.QueueRotation.Modifiers;

namespace KHost.UnitTests.Domain.Services.QueueRotation.Modifiers;

public class CoolDownModifierTests
{
    [Fact]
    public async Task FinishedSinger_PushedPastCoolDownSlots()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var carol = QueueRotationTestHelpers.User("Carol");
        var dave = QueueRotationTestHelpers.User("Dave");
        var config = new QueueRotationConfig { CoolDownSlots = 2 };
        var ctx = QueueRotationTestHelpers.Context([alice, bob, carol, dave], finishedSingerId: alice.Id, config: config);

        var result = await new CoolDownModifier(new IdentityInnerStrategy()).ApplyAsync(ctx);

        Assert.Equal(new[] { bob.Id, carol.Id, alice.Id, dave.Id }, result);
    }

    [Fact]
    public async Task FinishedSingerAlreadyPastCoolDown_PassesThrough()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var carol = QueueRotationTestHelpers.User("Carol");
        var config = new QueueRotationConfig { CoolDownSlots = 1 };
        var ctx = QueueRotationTestHelpers.Context([alice, bob, carol], finishedSingerId: carol.Id, config: config);

        var result = await new CoolDownModifier(new IdentityInnerStrategy()).ApplyAsync(ctx);

        Assert.Equal(new[] { alice.Id, bob.Id, carol.Id }, result);
    }
}
