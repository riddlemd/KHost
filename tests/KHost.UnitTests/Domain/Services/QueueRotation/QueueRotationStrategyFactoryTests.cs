using KHost.Plugins.Sdk.Models.QueueRotation;
using KHost.Plugins.Sdk.Services.QueueRotation;
using KHost.Domain.Services.QueueRotation;
using KHost.Domain.Services.QueueRotation.Modes;

namespace KHost.UnitTests.Domain.Services.QueueRotation;

public class RotationStrategyFactoryTests
{
    private static QueueRotationStrategyFactory BuildFactory()
    {
        var modes = new IQueueRotationMode[]
        {
            new FifoStrategy(),
            new RoundRobinStrategy(),
            new ReverseStrategy(),
            new LongestWaitFirstStrategy(),
            new FewestSongsFirstStrategy(),
            new WeightedFairStrategy(),
            new PureLotteryStrategy(),
            new WeightedLotteryStrategy(),
            new ShuffleBucketStrategy(),
        };
        return new QueueRotationStrategyFactory(modes);
    }

    [Fact]
    public async Task BareConfig_ReturnsBaseModeOnly_FifoBehavior()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var ctx = QueueRotationTestHelpers.Context([alice, bob], finishedSingerId: alice.Id);

        var config = new QueueRotationConfig { StrategyId = "fifo" };
        var strategy = BuildFactory().Resolve(config);
        var result = await strategy.ApplyAsync(ctx);

        Assert.Equal(new[] { bob.Id, alice.Id }, result);
    }

    [Fact]
    public async Task PresenceWrapsCoolDownWrapsVipWrapsBase_OutermostWins()
    {
        var now = new DateTime(2026, 4, 28, 20, 0, 0, DateTimeKind.Utc);
        var vipGroupId = Guid.NewGuid();

        var aliceVipPresent = QueueRotationTestHelpers.User("AliceVip", lastCheckinOn: now.AddMinutes(-5), groupIds: vipGroupId);
        var bobPresent = QueueRotationTestHelpers.User("BobPresent", lastCheckinOn: now.AddMinutes(-5));
        var carolAbsent = QueueRotationTestHelpers.User("CarolAbsent", lastCheckinOn: now.AddMinutes(-60));

        var config = new QueueRotationConfig
        {
            StrategyId = "fifo",
            VipGroupId = vipGroupId,
            CoolDownSlots = 1,
            PresenceRequired = true,
            PresenceWindowMinutes = 30,
        };

        var ctx = QueueRotationTestHelpers.Context(
            [aliceVipPresent, bobPresent, carolAbsent],
            finishedSingerId: aliceVipPresent.Id,
            config: config,
            now: now);

        var strategy = BuildFactory().Resolve(config);
        var result = await strategy.ApplyAsync(ctx);

        Assert.Equal(carolAbsent.Id, result[^1]);
        Assert.NotEqual(aliceVipPresent.Id, result[0]);
    }

    [Fact]
    public async Task FifoJoin_PlacesNewSingerAtEnd()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var carol = QueueRotationTestHelpers.User("Carol");

        var ctx = QueueRotationTestHelpers.Context(
            [alice, bob, carol],
            joiningSingerId: carol.Id,
            config: new QueueRotationConfig { StrategyId = "fifo" });

        var strategy = BuildFactory().Resolve(new QueueRotationConfig { StrategyId = "fifo" });
        var result = await strategy.ApplyAsync(ctx);

        Assert.Equal(carol.Id, result[^1]);
    }

    [Fact]
    public async Task RoundRobinJoin_PlacesNewSingerAtEnd()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var carol = QueueRotationTestHelpers.User("Carol");

        var ctx = QueueRotationTestHelpers.Context(
            [alice, bob, carol],
            joiningSingerId: carol.Id,
            config: new QueueRotationConfig { StrategyId = "round-robin" });

        var strategy = BuildFactory().Resolve(new QueueRotationConfig { StrategyId = "round-robin" });
        var result = await strategy.ApplyAsync(ctx);

        Assert.Equal(carol.Id, result[^1]);
    }

    [Fact]
    public async Task ReverseJoin_PlacesNewSingerAtFront()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var carol = QueueRotationTestHelpers.User("Carol");

        var ctx = QueueRotationTestHelpers.Context(
            [alice, bob, carol],
            joiningSingerId: carol.Id,
            config: new QueueRotationConfig { StrategyId = "reverse" });

        var strategy = BuildFactory().Resolve(new QueueRotationConfig { StrategyId = "reverse" });
        var result = await strategy.ApplyAsync(ctx);

        Assert.Equal(carol.Id, result[0]);
    }

    [Fact]
    public async Task FewestSongsFirstJoin_SortsNewSingerByCount()
    {
        var alice = QueueRotationTestHelpers.User("Alice");
        var bob = QueueRotationTestHelpers.User("Bob");
        var carol = QueueRotationTestHelpers.User("Carol");

        var songsSung = new Dictionary<Guid, int>
        {
            { alice.Id, 3 },
            { bob.Id, 2 },
            { carol.Id, 0 }
        };

        var ctx = QueueRotationTestHelpers.Context(
            [alice, bob, carol],
            joiningSingerId: carol.Id,
            config: new QueueRotationConfig { StrategyId = "fewest-songs-first" },
            songsSung: songsSung);

        var strategy = BuildFactory().Resolve(new QueueRotationConfig { StrategyId = "fewest-songs-first" });
        var result = await strategy.ApplyAsync(ctx);

        Assert.Equal(carol.Id, result[0]);
        Assert.Equal(bob.Id, result[1]);
        Assert.Equal(alice.Id, result[2]);
    }
}
