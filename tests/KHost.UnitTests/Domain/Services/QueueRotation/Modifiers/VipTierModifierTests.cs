using KHost.Plugins.Sdk.Models.QueueRotation;
using KHost.Domain.Services.QueueRotation.Modifiers;

namespace KHost.UnitTests.Domain.Services.QueueRotation.Modifiers;

public class VipTierModifierTests
{
    [Fact]
    public async Task VipMembers_MoveToFront_PreservingInnerOrder()
    {
        var vipGroupId = Guid.NewGuid();
        var alice = QueueRotationTestHelpers.User("Alice");
        var bobVip = QueueRotationTestHelpers.User("Bob", groupIds: vipGroupId);
        var carol = QueueRotationTestHelpers.User("Carol");
        var daveVip = QueueRotationTestHelpers.User("Dave", groupIds: vipGroupId);
        var config = new QueueRotationConfig { VipGroupId = vipGroupId };
        var ctx = QueueRotationTestHelpers.Context([alice, bobVip, carol, daveVip], config: config);

        var result = await new VipTierModifier(new IdentityInnerStrategy()).ApplyAsync(ctx);

        Assert.Equal(new[] { bobVip.Id, daveVip.Id, alice.Id, carol.Id }, result);
    }

    [Fact]
    public async Task NoVipGroupConfigured_PassesThrough()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var ctx = QueueRotationTestHelpers.Context([alice, bob]);

        var result = await new VipTierModifier(new IdentityInnerStrategy()).ApplyAsync(ctx);

        Assert.Equal(new[] { alice.Id, bob.Id }, result);
    }
}
