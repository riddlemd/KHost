using Microsoft.Extensions.Logging.Abstractions;
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
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>Regression: the add form used to call AddUserAsync then SelectUserAsync as two
/// separate service calls, so typing a singer's name and hitting add redrew the room's screens
/// and both queue panels twice. Uses a real SingerQueueService and a real broker — a substituted
/// ISingerQueueService never actually announces, so it could not catch this.</summary>
public class SingerQueuePanelAddSingerAnnounceTests : BunitContext
{
    private const string InputSelector = "input.kh-combobox__input";
    private const string FormSelector = "form[name='add-singer-form']";

    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"khost-panel-add-{Guid.NewGuid():N}");
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly Dictionary<Guid, KHostUser> _userDb = [];

    public SingerQueuePanelAddSingerAnnounceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var analytics = Substitute.For<IAnalyticsService>();
        var cache = new JsonFileCacheService(NullLogger<JsonFileCacheService>.Instance, analytics, _cacheDir);

        var performances = Substitute.For<IPerformanceService>();
        performances.ReadQueuedAsync().Returns([]);
        performances.CountSungSinceAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<DateTime>())
            .Returns(new Dictionary<Guid, int>());
        performances.ReadBySingerIdAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<PerformanceFilter>(), Arg.Any<DateTime?>())
            .Returns(new PaginatedResult<Performance>());

        var venues = Substitute.For<IVenuesService>();
        venues.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(new PaginatedResult<Venue>());
        venues.ReadSelectedVenueAsync().Returns((Venue?)null);

        var users = Substitute.For<IUsersService>();
        users.ReadAsync(Arg.Any<Guid>()).Returns(callInfo =>
        {
            _userDb.TryGetValue((Guid)callInfo[0], out var user);
            return Task.FromResult(user);
        });
        // The panel's own add flow: no match by exact name, so it creates a new singer.
        users.SearchAsync(Arg.Any<string>()).Returns(new PaginatedResult<KHostUser>());
        users.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<UserSearchOptions?>())
            .Returns(new PaginatedResult<KHostUser>());
        users.CreateAsync(Arg.Any<KHostUser>()).Returns(callInfo =>
        {
            var user = (KHostUser)callInfo[0];
            _userDb[user.Id] = user;
            return Task.FromResult(user);
        });

        var rotationFactory = Substitute.For<IQueueRotationStrategyFactory>();
        var strategy = Substitute.For<IQueueRotationStrategy>();
        strategy.ApplyAsync(Arg.Any<QueueRotationContext>())
            .Returns(args => Task.FromResult<IReadOnlyList<Guid>>(
                ((QueueRotationContext)args[0]).Queue.Select(u => u.Id).ToList()));
        rotationFactory.Resolve(Arg.Any<QueueRotationConfig>()).Returns(strategy);

        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        var queue = new SingerQueueService(
            NullLogger<SingerQueueService>.Instance, cache, performances, users, venues, analytics, rotationFactory, _broker);

        Services.AddSingleton<ISingerQueueService>(queue);
        Services.AddSingleton<IMessageBroker>(_broker);
        Services.AddSingleton(permissions);
        Services.AddSingleton(venues);
        Services.AddSingleton(performances);
        Services.AddSingleton(users);
        Services.AddSingleton(Substitute.For<IMediaService>());
        Services.AddSingleton(Substitute.For<IPlaybackService>());
        Services.AddSingleton(Substitute.For<IDialogService>());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Directory.Exists(_cacheDir))
            Directory.Delete(_cacheDir, recursive: true);

        base.Dispose(disposing);
    }

    [Fact]
    public async Task TypingANameAndSubmitting_AnnouncesSingerQueueChangedExactlyOnce()
    {
        var announceCount = 0;
        using var subscription = _broker.Subscribe<SingerQueueChanged>(_ => announceCount++);

        var cut = Render<SingerQueuePanel>();

        cut.Find(InputSelector).Input("Zoe");
        cut.Find(FormSelector).Submit();

        // PublishChanged is fire-and-forget by design (never blocks a handler that calls back in),
        // so the broker's own callback can still be in flight right after Submit returns.
        await Task.Delay(50);

        Assert.Single(_userDb.Values, u => u.Name == "Zoe");
        Assert.Equal(1, announceCount);
        Assert.Equal(_userDb.Values.Single(u => u.Name == "Zoe").Id,
            Services.GetRequiredService<ISingerQueueService>().SelectedUserId);
    }
}
