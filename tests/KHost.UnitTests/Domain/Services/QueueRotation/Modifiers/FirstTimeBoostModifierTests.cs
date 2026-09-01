using KHost.Abstractions.Models.QueueRotation;
using KHost.Domain.Services.QueueRotation.Modifiers;

namespace KHost.UnitTests.Domain.Services.QueueRotation.Modifiers;

public class FirstTimeBoostModifierTests
{
    [Fact]
    public async Task FirstTimer_BoostedAheadOfRegulars()
    {
        var now = new DateTime(2026, 4, 28, 20, 0, 0, DateTimeKind.Utc);
        var regular = QueueRotationTestHelpers.User("Regular", lastSangOn: now.AddMinutes(-10));
        var firstTimer = QueueRotationTestHelpers.User("Newcomer");
        var anotherRegular = QueueRotationTestHelpers.User("Another", lastSangOn: now.AddMinutes(-5));

        var config = new QueueRotationConfig { FirstTimeBoostEnabled = true, FirstTimeBoostSlots = 1 };
        var ctx = QueueRotationTestHelpers.Context([regular, anotherRegular, firstTimer], config: config, now: now);

        var result = await new FirstTimeBoostModifier(new IdentityInnerStrategy()).ApplyAsync(ctx);

        Assert.Equal(regular.Id, result[0]);
        Assert.Equal(firstTimer.Id, result[1]);
        Assert.Equal(anotherRegular.Id, result[2]);
    }

    [Fact]
    public async Task BoostDisabled_PassesThrough()
    {
        var firstTimer = QueueRotationTestHelpers.User("Newcomer");
        var regular = QueueRotationTestHelpers.User("Regular", lastSangOn: DateTime.UtcNow.AddMinutes(-5));
        var ctx = QueueRotationTestHelpers.Context([regular, firstTimer]);

        var result = await new FirstTimeBoostModifier(new IdentityInnerStrategy()).ApplyAsync(ctx);

        Assert.Equal(new[] { regular.Id, firstTimer.Id }, result);
    }
}
