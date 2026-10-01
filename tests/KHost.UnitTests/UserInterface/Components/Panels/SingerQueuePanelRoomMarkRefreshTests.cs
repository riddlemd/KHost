using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Models.QueueRotation;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.QueueRotation;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>The room mark follows a guest's key as it comes and goes, without the queue itself
/// having to move first.</summary>
public class SingerQueuePanelRoomMarkRefreshTests : BunitContext
{
    private const string RemoteMarkSelector = ".kh-singer-queue-panel__singer-queue__singer__remote";

    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"khost-queue-mark-{Guid.NewGuid():N}");
    private readonly Dictionary<Guid, KHostUser> _userDb = [];
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly SingerQueueService _queue;
    private readonly KHostUser _bob = new() { Name = "Bob" };

    public SingerQueuePanelRoomMarkRefreshTests()
    {
        var users = Substitute.For<IUsersService>();
        users.ReadAsync(Arg.Any<Guid>())
            .Returns(args => { _userDb.TryGetValue((Guid)args[0], out var u); return Task.FromResult(u); });

        var performances = Substitute.For<IPerformanceService>();
        performances.ReadQueuedAsync().Returns([]);
        performances.CountSungSinceAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<DateTime>())
            .Returns(new Dictionary<Guid, int>());

        var strategy = Substitute.For<IQueueRotationStrategy>();
        strategy.ApplyAsync(Arg.Any<QueueRotationContext>())
            .Returns(args => Task.FromResult<IReadOnlyList<Guid>>(((QueueRotationContext)args[0]).Queue.Select(u => u.Id).ToList()));
        var rotation = Substitute.For<IQueueRotationStrategyFactory>();
        rotation.Resolve(Arg.Any<QueueRotationConfig>()).Returns(strategy);

        var venues = Substitute.For<IVenuesService>();
        venues.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(new PaginatedResult<Venue>());

        var cache = new JsonFileCacheService(
            NullLogger<JsonFileCacheService>.Instance, Substitute.For<IAnalyticsService>(), _cacheDir);
        _queue = new SingerQueueService(NullLogger<SingerQueueService>.Instance, cache, performances, users,
            venues, Substitute.For<IAnalyticsService>(), rotation, _broker);

        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton<ISingerQueueService>(_queue);
        Services.AddSingleton<IMessageBroker>(_broker);
        Services.AddSingleton(permissions);
        Services.AddSingleton(venues);
        Services.AddSingleton(performances);
        Services.AddSingleton(Substitute.For<IMediaService>());
        Services.AddSingleton(Substitute.For<IPlaybackService>());
        Services.AddSingleton(users);
        Services.AddSingleton(Substitute.For<IDialogService>());
    }

    [Fact]
    public async Task TheMark_AppearsWhenAGuestKeyLands_AndGoesWhenItIsRemoved()
    {
        _userDb[_bob.Id] = _bob;
        await _queue.AddUserAsync(_bob.Id);

        var panel = Render<SingerQueuePanel>();
        Assert.Empty(panel.FindAll(RemoteMarkSelector));

        _userDb[_bob.Id] = Reread(new KHostUserForeignKey { Source = "Example", Key = "guest-7", IsEphemeral = true });
        _broker.Announce(new UsersChanged());

        panel.WaitForAssertion(() => Assert.Single(panel.FindAll(RemoteMarkSelector)), TimeSpan.FromSeconds(5));

        _userDb[_bob.Id] = Reread();
        _broker.Announce(new UsersChanged());

        panel.WaitForAssertion(() => Assert.Empty(panel.FindAll(RemoteMarkSelector)), TimeSpan.FromSeconds(5));
    }

    // A fresh object, as a real read returns: mutating the cached one would pass without a re-read.
    private KHostUser Reread(params KHostUserForeignKey[] keys)
        => new() { Id = _bob.Id, Name = _bob.Name, ForeignKeys = [.. keys] };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _queue.Dispose();
            if (Directory.Exists(_cacheDir))
                Directory.Delete(_cacheDir, recursive: true);
        }

        base.Dispose(disposing);
    }
}
