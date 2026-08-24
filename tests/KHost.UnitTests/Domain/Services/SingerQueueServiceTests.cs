using KHost.Plugins.Sdk.Models;
using KHost.Plugins.Sdk.Models.QueueRotation;
using KHost.Plugins.Sdk.Services;
using KHost.Plugins.Sdk.Services.QueueRotation;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.QueueRotation;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.QueueRotation;
using KHost.Domain.Services.QueueRotation.Modes;
using KHost.Plugins.Sdk.Messaging.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services;

[Collection(FileCacheCollection.Name)]
public class SingerQueueServiceTests : IDisposable
{
    private static readonly string _cacheDir = Path.Combine(AppContext.BaseDirectory, "cache");
    private readonly ICacheService _cacheService;
    private readonly IPerformanceService _performanceService;
    private readonly IUsersService _usersService;
    private readonly IVenuesService _venuesService;
    private readonly IQueueRotationStrategyFactory _rotationFactory;
    private readonly Dictionary<Guid, KHostUser> _userDb = [];
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly SingerQueueService _service;

    public SingerQueueServiceTests()
    {
        var analyticsCache = Substitute.For<IAnalyticsService>();
        _cacheService = new JsonFileCacheService(NullLogger<JsonFileCacheService>.Instance, analyticsCache);
        _performanceService = Substitute.For<IPerformanceService>();
        _usersService = Substitute.For<IUsersService>();
        _venuesService = Substitute.For<IVenuesService>();

        _performanceService.CreateAndEnqueueAsync(Arg.Any<Performance>())
            .Returns(args => Task.FromResult<Performance?>((Performance)args[0]));

        _usersService.ReadAsync(Arg.Any<Guid>())
            .Returns(args => { _userDb.TryGetValue((Guid)args[0], out var u); return Task.FromResult(u); });

        _usersService.UpdateAsync(Arg.Any<KHostUser>())
            .Returns(args => { var u = (KHostUser)args[0]; _userDb[u.Id] = u; return Task.CompletedTask; });

        _rotationFactory = Substitute.For<IQueueRotationStrategyFactory>();

        _performanceService.CountSungSinceAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<DateTime>())
            .Returns(new Dictionary<Guid, int>());
        _performanceService.ReadBySingerIdAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<PerformanceFilter>(), Arg.Any<DateTime?>())
            .Returns(new PaginatedResult<Performance>());

        // Rotation runs on every add and finish; identity keeps non-rotation tests order-stable.
        StubStrategy(ctx => ctx.Queue.Select(u => u.Id).ToList());

        var analyticsQueue = Substitute.For<IAnalyticsService>();
        _service = new SingerQueueService(NullLogger<SingerQueueService>.Instance, _cacheService, _performanceService, _usersService, _venuesService, analyticsQueue, _rotationFactory, _broker);
    }

    private void StubStrategy(Func<QueueRotationContext, IReadOnlyList<Guid>> strategy)
    {
        var stub = Substitute.For<IQueueRotationStrategy>();
        stub.ApplyAsync(Arg.Any<QueueRotationContext>())
            .Returns(args => Task.FromResult(strategy((QueueRotationContext)args[0])));

        _rotationFactory.Resolve(Arg.Any<QueueRotationConfig>()).Returns(stub);
    }

    public void Dispose()
    {
        var cacheFile = Path.Combine(_cacheDir, "singer-queue.json");
        if (File.Exists(cacheFile))
            File.Delete(cacheFile);
    }

    [Fact]
    public void NewService_StartsEmpty()
    {
        Assert.Empty(_service.Users);
        Assert.Null(_service.SelectedUserId);
        Assert.Null(_service.SelectedUser);
    }

    [Fact]
    public async Task AddUserAsync_AddsUserById()
    {
        var user = await EnqueueAsync("Alice");

        Assert.Single(_service.Users);
        Assert.Equal("Alice", user.Name);
        Assert.NotEqual(Guid.Empty, user.Id);
    }

    [Fact]
    public async Task AddUserAsync_AnnouncesSingerQueueChanged()
    {
        var raised = false;
        using var subscription = _broker.Subscribe<SingerQueueChanged>(_ => raised = true);

        await EnqueueAsync("Alice");

        Assert.True(raised);
    }

    [Fact]
    public async Task RemoveUserAsync_RemovesUser()
    {
        var alice = await EnqueueAsync("Alice");

        await _service.RemoveUserAsync(alice.Id);

        Assert.Empty(_service.Users);
    }

    [Fact]
    public async Task RemoveUserAsync_ClearsSelection_IfRemovingSelected()
    {
        var alice = await EnqueueAsync("Alice");
        await _service.SelectUserAsync(alice.Id);

        await _service.RemoveUserAsync(alice.Id);

        Assert.Null(_service.SelectedUserId);
    }

    [Fact]
    public async Task SelectUserAsync_SetsSelectedId()
    {
        var alice = await EnqueueAsync("Alice");
        var bob = await EnqueueAsync("Bob");

        await _service.SelectUserAsync(bob.Id);

        Assert.Equal(bob.Id, _service.SelectedUserId);
        Assert.Equal("Bob", _service.SelectedUser!.Name);
    }

    private static MediaSearchEntity SearchResult(string foreignKey) => new()
    {
        SourceDisplayName = "FileSystem",
        Source = "FileSystem",
        ForeignKey = foreignKey,
        Title = "My Media",
    };

    [Fact]
    public async Task AddMediaAsync_EnqueuesNothing_ForASingerNotInTheQueue()
    {
        await _service.AddMediaAsync(Guid.NewGuid(), SearchResult(Guid.NewGuid().ToString()));

        await _performanceService.DidNotReceive().CreateAndEnqueueAsync(Arg.Any<Performance>());
    }

    [Fact]
    public async Task AddMediaAsync_EnqueuesTheMediaTheResultNames()
    {
        var alice = await EnqueueAsync("Alice");
        var mediaId = Guid.NewGuid();

        await _service.AddMediaAsync(alice.Id, SearchResult(mediaId.ToString()));

        await _performanceService.Received(1).CreateAndEnqueueAsync(
            Arg.Is<Performance>(p => p.SingerId == alice.Id && p.MediaId == mediaId));
    }

    [Fact]
    public async Task AddMediaAsync_StampsTheEnqueueTimeInUtc()
    {
        var alice = await EnqueueAsync("Alice");
        var before = DateTime.UtcNow;

        await _service.AddMediaAsync(alice.Id, SearchResult(Guid.NewGuid().ToString()));

        await _performanceService.Received(1).CreateAndEnqueueAsync(
            Arg.Is<Performance>(p => p.CreatedDate >= before && p.CreatedDate <= DateTime.UtcNow));
    }

    // A remote provider's key is its own — a video id, a URL — and names nothing in the library.
    [Theory]
    [InlineData("/music/media.mp4")]
    [InlineData("dQw4w9WgXcQ")]
    [InlineData("")]
    public async Task AddMediaAsync_EnqueuesNothing_WhenTheKeyIsNotALibraryMediaId(string foreignKey)
    {
        var alice = await EnqueueAsync("Alice");

        await _service.AddMediaAsync(alice.Id, SearchResult(foreignKey));

        await _performanceService.DidNotReceive().CreateAndEnqueueAsync(Arg.Any<Performance>());
    }

    [Fact]
    public async Task MoveUserUpAsync_SwapsWithPrevious()
    {
        var a = await EnqueueAsync("A");
        var b = await EnqueueAsync("B");

        await _service.MoveUserUpAsync(b.Id);

        Assert.Equal(b.Id, _service.Users[0].Id);
        Assert.Equal(a.Id, _service.Users[1].Id);
    }

    [Fact]
    public async Task MoveUserUpAsync_DoesNothing_ForFirst()
    {
        var a = await EnqueueAsync("A");
        var b = await EnqueueAsync("B");

        await _service.MoveUserUpAsync(a.Id);

        Assert.Equal(a.Id, _service.Users[0].Id);
        Assert.Equal(b.Id, _service.Users[1].Id);
    }

    [Fact]
    public async Task MoveUserDownAsync_SwapsWithNext()
    {
        var a = await EnqueueAsync("A");
        var b = await EnqueueAsync("B");

        await _service.MoveUserDownAsync(a.Id);

        Assert.Equal(b.Id, _service.Users[0].Id);
        Assert.Equal(a.Id, _service.Users[1].Id);
    }

    [Fact]
    public async Task MoveUserDownAsync_DoesNothing_ForLast()
    {
        var a = await EnqueueAsync("A");
        var b = await EnqueueAsync("B");

        await _service.MoveUserDownAsync(b.Id);

        Assert.Equal(a.Id, _service.Users[0].Id);
        Assert.Equal(b.Id, _service.Users[1].Id);
    }

    [Fact]
    public async Task MoveUserToStartAsync_MovesToFirst()
    {
        await EnqueueAsync("A");
        await EnqueueAsync("B");
        var c = await EnqueueAsync("C");

        await _service.MoveUserToStartAsync(c.Id);

        Assert.Equal(c.Id, _service.Users[0].Id);
        Assert.Equal(3, _service.Users.Count);
    }

    [Fact]
    public async Task MoveUserToEndAsync_MovesToLast()
    {
        var a = await EnqueueAsync("A");
        await EnqueueAsync("B");
        await EnqueueAsync("C");

        await _service.MoveUserToEndAsync(a.Id);

        Assert.Equal(a.Id, _service.Users[^1].Id);
        Assert.Equal(3, _service.Users.Count);
    }

    [Fact]
    public async Task SelectFirstUserInQueueAsync_DoesNothing_WhenEmpty()
    {
        await _service.SelectFirstUserInQueueAsync();

        Assert.Null(_service.SelectedUserId);
    }

    [Fact]
    public async Task SelectFirstUserInQueueAsync_SelectsFirst()
    {
        var a = await EnqueueAsync("A");
        await EnqueueAsync("B");

        await _service.SelectFirstUserInQueueAsync();

        Assert.Equal(a.Id, _service.SelectedUserId);
    }

    [Fact]
    public async Task SelectFirstUserInQueueAsync_SelectsFirst_WhenMultipleUsersExist()
    {
        var a = await EnqueueAsync("A");
        var b = await EnqueueAsync("B");

        await _service.SelectFirstUserInQueueAsync();

        Assert.Equal(a.Id, _service.SelectedUserId);
    }

    [Fact]
    public async Task MoveUserUpAsync_BlockedFromIndex1_WhenTopSlotLockedForOther()
    {
        var a = await EnqueueAsync("A");
        var b = await EnqueueAsync("B");

        _service.LockTopSlot();

        await _service.MoveUserUpAsync(b.Id);

        Assert.Equal(a.Id, _service.Users[0].Id);
        Assert.Equal(b.Id, _service.Users[1].Id);
    }

    [Fact]
    public async Task MoveUserUpAsync_AllowedFromIndex2_WhenTopSlotLocked()
    {
        var a = await EnqueueAsync("A");
        var b = await EnqueueAsync("B");
        var c = await EnqueueAsync("C");

        _service.LockTopSlot();

        await _service.MoveUserUpAsync(c.Id);

        Assert.Equal(a.Id, _service.Users[0].Id);
        Assert.Equal(c.Id, _service.Users[1].Id);
        Assert.Equal(b.Id, _service.Users[2].Id);
    }

    [Fact]
    public async Task MoveUserUpAsync_BlockedForLockedUser_Itself()
    {
        var a = await EnqueueAsync("A");
        var b = await EnqueueAsync("B");

        _service.LockTopSlot();

        await _service.MoveUserUpAsync(b.Id);

        Assert.Equal(a.Id, _service.Users[0].Id);
        Assert.Equal(b.Id, _service.Users[1].Id);
    }

    [Fact]
    public async Task MoveUserToStartAsync_Blocked_WhenTopSlotLockedForOther()
    {
        var a = await EnqueueAsync("A");
        await EnqueueAsync("B");
        var c = await EnqueueAsync("C");

        _service.LockTopSlot();

        await _service.MoveUserToStartAsync(c.Id);

        Assert.Equal(a.Id, _service.Users[0].Id);
        Assert.Equal(c.Id, _service.Users[2].Id);
    }

    [Fact]
    public async Task MoveUserToStartAsync_BlockedForLockedUser_Itself()
    {
        var a = await EnqueueAsync("A");
        await EnqueueAsync("B");
        var c = await EnqueueAsync("C");

        _service.LockTopSlot();

        await _service.MoveUserToStartAsync(c.Id);

        Assert.Equal(a.Id, _service.Users[0].Id);
        Assert.Equal(c.Id, _service.Users[2].Id);
    }

    [Fact]
    public async Task UnlockTopSlot_ReleasesGuard()
    {
        var a = await EnqueueAsync("A");
        var b = await EnqueueAsync("B");

        _service.LockTopSlot();
        _service.UnlockTopSlot();

        await _service.MoveUserUpAsync(b.Id);

        Assert.Equal(b.Id, _service.Users[0].Id);
        Assert.Equal(a.Id, _service.Users[1].Id);
        Assert.False(_service.IsTopSlotLocked);
    }

    [Fact]
    public async Task MoveUserToIndexAsync_MovesUserToSpecifiedPosition()
    {
        var a = await EnqueueAsync("A");
        var b = await EnqueueAsync("B");
        var c = await EnqueueAsync("C");

        await _service.MoveUserToIndexAsync(c.Id, 0);

        Assert.Equal(c.Id, _service.Users[0].Id);
        Assert.Equal(a.Id, _service.Users[1].Id);
        Assert.Equal(b.Id, _service.Users[2].Id);
    }

    [Fact]
    public async Task MoveUserToIndexAsync_ClampsIndexToValidRange()
    {
        var a = await EnqueueAsync("A");
        var b = await EnqueueAsync("B");

        await _service.MoveUserToIndexAsync(a.Id, 99);

        Assert.Equal(b.Id, _service.Users[0].Id);
        Assert.Equal(a.Id, _service.Users[1].Id);
    }

    [Fact]
    public async Task MoveUserToIndexAsync_DoesNothing_WhenUserNotInQueue()
    {
        var a = await EnqueueAsync("A");
        var b = await EnqueueAsync("B");

        await _service.MoveUserToIndexAsync(Guid.NewGuid(), 0);

        Assert.Equal(a.Id, _service.Users[0].Id);
        Assert.Equal(b.Id, _service.Users[1].Id);
    }

    [Fact]
    public async Task MoveUserToIndexAsync_DoesNotMoveToZero_WhenTopSlotLocked()
    {
        var a = await EnqueueAsync("A");
        var b = await EnqueueAsync("B");

        _service.LockTopSlot();
        await _service.MoveUserToIndexAsync(b.Id, 0);

        Assert.Equal(a.Id, _service.Users[0].Id);
        Assert.Equal(b.Id, _service.Users[1].Id);
    }

    [Fact]
    public async Task ClearAsync_ClearsQueueAndDelegatesPerformanceDelete_WhenVenueSettingIsTrue()
    {
        _venuesService.ReadSelectedVenueAsync()
            .Returns(new Venue { Name = "Test", Settings = new Venue.VenueSettings { ClearQueueOnClose = true } });

        await EnqueueAsync("Alice");
        await EnqueueAsync("Bob");

        await _service.ClearAsync();

        Assert.Null(_service.SelectedUserId);
        await _performanceService.Received(1).DeleteAllQueuedAsync();
    }

    [Fact]
    public async Task ClearAsync_DoesNothing_WhenVenueSettingIsFalse()
    {
        _venuesService.ReadSelectedVenueAsync()
            .Returns(new Venue { Name = "Test", Settings = new Venue.VenueSettings { ClearQueueOnClose = false } });

        await EnqueueAsync("Alice");

        await _service.ClearAsync();

        Assert.Single(_service.Users);
        await _performanceService.DidNotReceive().DeleteAllQueuedAsync();
    }

    [Fact]
    public async Task InitializeAsync_DoesNothing_WhenCacheIsEmpty()
    {
        var fresh = CreateFreshService();

        await fresh.InitializeAsync();

        Assert.Empty(fresh.Users);
        Assert.Null(fresh.SelectedUserId);
    }

    [Fact]
    public async Task InitializeAsync_RestoresQueueFromCache()
    {
        await EnqueueAsync("Alice");
        await EnqueueAsync("Bob");

        var fresh = CreateFreshService();
        await fresh.InitializeAsync();

        Assert.Equal(2, fresh.Users.Count);
    }

    [Fact]
    public async Task InitializeAsync_RestoresSelectedUserFromCache()
    {
        var alice = await EnqueueAsync("Alice");
        await EnqueueAsync("Bob");
        await _service.SelectUserAsync(alice.Id);

        var fresh = CreateFreshService();
        await fresh.InitializeAsync();

        Assert.Equal(alice.Id, fresh.SelectedUserId);
    }

    private SingerQueueService CreateFreshService() => new(
        NullLogger<SingerQueueService>.Instance,
        _cacheService,
        _performanceService,
        _usersService,
        _venuesService,
        Substitute.For<IAnalyticsService>(),
        _rotationFactory);

    [Fact]
    public async Task RotateQueueAsync_DefaultConfig_MovesFinishedSingerToEndAndSelectsFirst()
    {
        // Real fifo strategy: no venue config at all must behave like classic rotation.
        var service = CreateServiceWithRealFifo();
        var alice = await EnqueueUserOnAsync(service, "Alice");
        var bob = await EnqueueUserOnAsync(service, "Bob");

        await service.RotateQueueAsync(alice.Id);

        Assert.Equal([bob.Id, alice.Id], service.Users.Select(u => u.Id));
        Assert.Equal(bob.Id, service.SelectedUserId);
    }

    [Fact]
    public async Task RotateQueueAsync_FifoLeavesQueueDropPosition_RemovesFinishedSinger()
    {
        _venuesService.ReadSelectedVenueAsync().Returns(new Venue
        {
            Id = Guid.NewGuid(),
            Name = "Test Venue",
            Settings = new Venue.VenueSettings
            {
                QueueRotation = new QueueRotationConfig { DropPosition = DropPositionMode.LeavesQueue },
            },
        });
        var service = CreateServiceWithRealFifo();
        var alice = await EnqueueUserOnAsync(service, "Alice");
        var bob = await EnqueueUserOnAsync(service, "Bob");

        await service.RotateQueueAsync(alice.Id);

        Assert.Equal([bob.Id], service.Users.Select(u => u.Id));
        Assert.Equal(bob.Id, service.SelectedUserId);
    }

    [Fact]
    public async Task RotateQueueAsync_AppliesStrategyOrderAndSelectsFirst()
    {
        var alice = await EnqueueAsync("Alice");
        var bob = await EnqueueAsync("Bob");
        var carol = await EnqueueAsync("Carol");
        StubStrategy(ctx => ctx.Queue.Select(u => u.Id).Reverse().ToList());

        await _service.RotateQueueAsync(alice.Id);

        Assert.Equal([carol.Id, bob.Id, alice.Id], _service.Users.Select(u => u.Id));
        Assert.Equal(carol.Id, _service.SelectedUserId);
    }

    [Fact]
    public async Task RotateQueueAsync_StrategyDropsFinishedSinger_SingerLeavesQueue()
    {
        var alice = await EnqueueAsync("Alice");
        var bob = await EnqueueAsync("Bob");
        StubStrategy(ctx => ctx.Queue.Select(u => u.Id).Where(id => id != ctx.FinishedSingerId).ToList());

        await _service.RotateQueueAsync(alice.Id);

        Assert.Equal([bob.Id], _service.Users.Select(u => u.Id));
    }

    [Fact]
    public async Task RotateQueueAsync_StrategyDropsOtherSingerAndInventsIds_QueueSanitized()
    {
        var alice = await EnqueueAsync("Alice");
        var bob = await EnqueueAsync("Bob");
        var carol = await EnqueueAsync("Carol");
        // Keeps the finished singer but drops Carol, duplicates Bob, and invents an id.
        StubStrategy(_ => [Guid.NewGuid(), bob.Id, bob.Id, alice.Id]);

        await _service.RotateQueueAsync(alice.Id);

        Assert.Equal([bob.Id, alice.Id, carol.Id], _service.Users.Select(u => u.Id));
    }

    [Fact]
    public async Task RotateQueueAsync_StrategyThrows_OrderUnchanged()
    {
        var alice = await EnqueueAsync("Alice");
        var bob = await EnqueueAsync("Bob");
        var stub = Substitute.For<IQueueRotationStrategy>();
        stub.ApplyAsync(Arg.Any<QueueRotationContext>())
            .Returns<Task<IReadOnlyList<Guid>>>(_ => throw new InvalidOperationException("plugin bug"));
        _rotationFactory.Resolve(Arg.Any<QueueRotationConfig>()).Returns(stub);

        await _service.RotateQueueAsync(alice.Id);

        Assert.Equal([alice.Id, bob.Id], _service.Users.Select(u => u.Id));
    }

    [Fact]
    public async Task AddUserAsync_AppliesJoinPlacement()
    {
        var alice = await EnqueueAsync("Alice");
        var bob = await EnqueueAsync("Bob");
        StubStrategy(ctx => ctx.JoiningSingerId is { } joiner
            ? [joiner, .. ctx.Queue.Select(u => u.Id).Where(id => id != joiner)]
            : ctx.Queue.Select(u => u.Id).ToList());

        var carol = await EnqueueAsync("Carol");

        Assert.Equal([carol.Id, alice.Id, bob.Id], _service.Users.Select(u => u.Id));
    }

    private SingerQueueService CreateServiceWithRealFifo() => new(
        NullLogger<SingerQueueService>.Instance,
        _cacheService,
        _performanceService,
        _usersService,
        _venuesService,
        Substitute.For<IAnalyticsService>(),
        new QueueRotationStrategyFactory([new FifoStrategy()]));

    private async Task<KHostUser> EnqueueUserOnAsync(SingerQueueService service, string name)
    {
        var user = new KHostUser { Name = name };
        _userDb[user.Id] = user;
        await service.AddUserAsync(user.Id);
        return user;
    }

    private async Task<KHostUser> EnqueueAsync(string name)
    {
        var user = new KHostUser { Name = name };
        _userDb[user.Id] = user;
        await _service.AddUserAsync(user.Id);
        return user;
    }
}
