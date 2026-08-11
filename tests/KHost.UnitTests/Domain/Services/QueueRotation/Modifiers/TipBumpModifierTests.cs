using KHost.Plugins.Sdk.Models.QueueRotation;
using KHost.Domain.Services.QueueRotation.Modifiers;

namespace KHost.UnitTests.Domain.Services.QueueRotation.Modifiers;

public class TipBumpModifierTests
{
    [Fact]
    public async Task RecentTipper_BumpedOneSlot()
    {
        var now = new DateTime(2026, 4, 28, 20, 0, 0, DateTimeKind.Utc);
        var alice = QueueRotationTestHelpers.User("Alice");
        var bobTipper = QueueRotationTestHelpers.User("Bob", lastTippedOn: now.AddMinutes(-2));
        var carol = QueueRotationTestHelpers.User("Carol");

        var config = new QueueRotationConfig { TipBumpWindowMinutes = 5 };
        var ctx = QueueRotationTestHelpers.Context([alice, bobTipper, carol], config: config, now: now);

        var result = await new TipBumpModifier(new IdentityInnerStrategy()).ApplyAsync(ctx);

        Assert.Equal(new[] { bobTipper.Id, alice.Id, carol.Id }, result);
    }

    [Fact]
    public async Task OldTip_OutsideWindow_NotBumped()
    {
        var now = new DateTime(2026, 4, 28, 20, 0, 0, DateTimeKind.Utc);
        var alice = QueueRotationTestHelpers.User("Alice");
        var bobOldTip = QueueRotationTestHelpers.User("Bob", lastTippedOn: now.AddMinutes(-30));

        var config = new QueueRotationConfig { TipBumpWindowMinutes = 5 };
        var ctx = QueueRotationTestHelpers.Context([alice, bobOldTip], config: config, now: now);

        var result = await new TipBumpModifier(new IdentityInnerStrategy()).ApplyAsync(ctx);

        Assert.Equal(new[] { alice.Id, bobOldTip.Id }, result);
    }
}
