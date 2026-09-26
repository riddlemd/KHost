using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.Displays.LocalScreen;
using KHost.Domain.Services.Messaging;
using KHost.IPC.SignalR.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services.Displays.LocalScreen;

/// <summary>The marquee follows the queue after the screen joined, not only at the join.</summary>
/// <remarks>Real broker, real up-next list and real provider, so the path under test is the one a
/// queue edit takes in the app: the producer's message, the up-next announcement, the redraw.</remarks>
public class LocalScreenMarqueeRedrawTests : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(50);

    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly IScreenServer _screenServer = Substitute.For<IScreenServer>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly UpNextService _upNext;
    private readonly LocalScreenDisplayProvider _provider;

    private volatile List<KHostUser> _singers = [Singer("Ada"), Singer("Bo")];

    public LocalScreenMarqueeRedrawTests()
    {
        _queue.Users.Returns(_ => _singers);
        _performances.ReadQueuedAsync().Returns([]);
        _venues.ReadSelectedVenueAsync().Returns(new Venue
        {
            Name = "The Bar",
            Settings = new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 3 },
        });

        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IPlaybackService>());
        services.AddSingleton<IUpNextService>(_ => _upNext!);
        var provider = services.BuildServiceProvider();

        _upNext = new UpNextService(
            NullLogger<UpNextService>.Instance, _broker, _venues, _queue, _performances, _media, provider, Settle);

        _provider = new LocalScreenDisplayProvider(
            NullLogger<LocalScreenDisplayProvider>.Instance, _screenServer, [], _broker, _venues,
            services: provider, redrawSettle: Settle);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _upNext.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SingerQueueChanged_AfterTheScreenJoined_ResendsTheMarqueeWithTheNewSingers()
    {
        await JoinAsync();

        _singers = [Singer("Ada"), Singer("Bo"), Singer("Cy")];
        _broker.Announce(new SingerQueueChanged());

        Assert.True(await WaitUntilAsync(() => Sent().Any(m => Named(m) == "Ada, Bo, Cy")));
    }

    [Fact]
    public async Task SingerQueueChanged_WithTheListUnchanged_SendsNoSecondMarquee()
    {
        await JoinAsync();

        _broker.Announce(new SingerQueueChanged());
        await Task.Delay(Settle * 8);

        Assert.Empty(Sent());
    }

    /// <summary>Joins a screen and waits out the full draw a join sends, then forgets it.</summary>
    private async Task JoinAsync()
    {
        var connection = Substitute.For<IScreenConnection>();
        connection.ScreenId.Returns(LocalScreenDisplayProvider.LocalScreenId);
        connection.ConnectionId.Returns("conn-a");
        connection.IsConnected.Returns(true);

        _screenServer.ScreenConnected += Raise.EventWith(
            _screenServer, new ScreenConnectionEventArgs { Connection = connection });

        Assert.True(await WaitUntilAsync(() => Sent().Any(m => Named(m) == "Ada, Bo")));
        await Task.Delay(Settle * 4);
        _screenServer.ClearReceivedCalls();
    }

    private static KHostUser Singer(string name) => new() { Id = Guid.NewGuid(), Name = name };

    // Names only: these tests never queue a song, so each turn is one plain Singer segment and the
    // real divider (a "•", not ", ") never enters into what this compares.
    private static string Named(SetMarqueeCommand marquee)
        => string.Join(", ", marquee.Entries.Where(s => s.Kind != MarqueeSegmentKind.Separator).Select(s => s.Text));

    private List<SetMarqueeCommand> Sent()
        => [.. _screenServer.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IScreenServer.BroadcastCommandAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<SetMarqueeCommand>()];

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }

        return false;
    }
}
