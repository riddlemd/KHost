using KHost.Plugins.Sdk.Models.QueueRotation;
using KHost.Domain.Services.QueueRotation.Modifiers;

namespace KHost.UnitTests.Domain.Services.QueueRotation.Modifiers;

public class TimeBoxedSlotsModifierTests
{
    [Fact]
    public async Task OverBudgetSingers_MovedToBack()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bobOverBudget = QueueRotationTestHelpers.User("Bob");
        var carol = QueueRotationTestHelpers.User("Carol");
        var songs = new Dictionary<Guid, int> { [bobOverBudget.Id] = 5 };

        var config = new QueueRotationConfig { TimeBoxMinutes = 12 };
        var ctx = QueueRotationTestHelpers.Context([alice, bobOverBudget, carol], config: config, songsSung: songs);

        var result = await new TimeBoxedSlotsModifier(new IdentityInnerStrategy()).ApplyAsync(ctx);

        Assert.Equal(new[] { alice.Id, carol.Id, bobOverBudget.Id }, result);
    }

    [Fact]
    public async Task UnderBudget_PassesThrough()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var songs = new Dictionary<Guid, int> { [bob.Id] = 1 };

        var config = new QueueRotationConfig { TimeBoxMinutes = 30 };
        var ctx = QueueRotationTestHelpers.Context([alice, bob], config: config, songsSung: songs);

        var result = await new TimeBoxedSlotsModifier(new IdentityInnerStrategy()).ApplyAsync(ctx);

        Assert.Equal(new[] { alice.Id, bob.Id }, result);
    }
}
