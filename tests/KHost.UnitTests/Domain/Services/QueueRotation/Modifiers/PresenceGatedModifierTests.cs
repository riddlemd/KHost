using KHost.Plugins.Sdk.Models.QueueRotation;
using KHost.Domain.Services.QueueRotation.Modifiers;

namespace KHost.UnitTests.Domain.Services.QueueRotation.Modifiers;

public class PresenceGatedModifierTests
{
    [Fact]
    public async Task AbsentSingers_MovedToBack()
    {
        var now = new DateTime(2026, 4, 28, 20, 0, 0, DateTimeKind.Utc);
        var present = QueueRotationTestHelpers.User("Present", lastCheckinOn: now.AddMinutes(-5));
        var absent = QueueRotationTestHelpers.User("Absent", lastCheckinOn: now.AddMinutes(-60));
        var neverCheckedIn = QueueRotationTestHelpers.User("Never");

        var config = new QueueRotationConfig { PresenceRequired = true, PresenceWindowMinutes = 30 };
        var ctx = QueueRotationTestHelpers.Context([absent, present, neverCheckedIn], config: config, now: now);

        var result = await new PresenceGatedModifier(new IdentityInnerStrategy()).ApplyAsync(ctx);

        Assert.Equal(present.Id, result[0]);
        Assert.Contains(absent.Id, result);
        Assert.Contains(neverCheckedIn.Id, result);
    }

    [Fact]
    public async Task PresenceNotRequired_PassesThrough()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var ctx = QueueRotationTestHelpers.Context([alice, bob]);

        var result = await new PresenceGatedModifier(new IdentityInnerStrategy()).ApplyAsync(ctx);

        Assert.Equal(new[] { alice.Id, bob.Id }, result);
    }
}
