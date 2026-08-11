using KHost.Plugins.Sdk.Models.QueueRotation;
using KHost.Domain.Services.QueueRotation.Modifiers;

namespace KHost.UnitTests.Domain.Services.QueueRotation.Modifiers;

public class LockedAnchorModifierTests
{
    [Fact]
    public async Task Anchor_AppearsAtConfiguredInterval()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var carol = QueueRotationTestHelpers.User("Carol");
        var dave = QueueRotationTestHelpers.User("Dave");
        var anchor = QueueRotationTestHelpers.User("Anchor");

        var config = new QueueRotationConfig { AnchorSingerId = anchor.Id, AnchorEveryN = 3 };
        var ctx = QueueRotationTestHelpers.Context([alice, bob, carol, dave, anchor], config: config);

        var result = await new LockedAnchorModifier(new IdentityInnerStrategy()).ApplyAsync(ctx);

        Assert.Equal(anchor.Id, result[0]);
        Assert.Equal(anchor.Id, result[3]);
    }

    [Fact]
    public async Task NoAnchorConfigured_PassesThrough()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var ctx = QueueRotationTestHelpers.Context([alice, bob]);

        var result = await new LockedAnchorModifier(new IdentityInnerStrategy()).ApplyAsync(ctx);

        Assert.Equal(new[] { alice.Id, bob.Id }, result);
    }
}
