using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Domain.Services;
using KHost.IPC.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.IPC;

// Uses the real ScreenServerService: it raises ScreenDisconnected while holding the same
// non-reentrant lock GetConnectedScreensAsync waits on, so an inline handler deadlocks the hub.
public class ScreenDisconnectPausesPlaybackTests : IDisposable
{
    private readonly ScreenServerService _screenServer;
    private readonly PlaybackService _playbackService;

    public ScreenDisconnectPausesPlaybackTests()
    {
        var hubContext = Substitute.For<IHubContext<ScreenHub>>();
        var clients = Substitute.For<IHubClients>();
        clients.Client(Arg.Any<string>()).Returns(Substitute.For<ISingleClientProxy>());
        clients.All.Returns(Substitute.For<IClientProxy>());
        hubContext.Clients.Returns(clients);

        _screenServer = new ScreenServerService(hubContext);

        var venues = Substitute.For<IVenuesService>();
        venues.ReadSelectedVenueAsync().Returns(new Venue
        {
            Id = Guid.NewGuid(),
            Name = "Test Venue",
            Settings = new Venue.VenueSettings(),
        });

        _playbackService = new PlaybackService(
            NullLogger<PlaybackService>.Instance,
            Substitute.For<ISingerQueueService>(),
            Substitute.For<IPerformanceService>(),
            venues,
            Substitute.For<IAnalyticsService>(),
            _screenServer,
            Options.Create(new PlaybackService.ServiceOptions { StopFadeDuration = TimeSpan.Zero }));
    }

    public void Dispose() => _playbackService.Dispose();

    private IHubCallback Callback => _screenServer;

    private async Task StartPlayingAsync()
    {
        var performance = new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = Guid.NewGuid() };
        var media = new Media
        {
            Id = performance.MediaId,
            Title = "Song",
            Artist = "Artist",
            FilePath = "/library/song.mp4",
            Format = "mp4",
            Duration = TimeSpan.FromMinutes(4),
        };

        await _playbackService.LoadAsync(performance, media);
        await _playbackService.PlayAsync();

        Assert.Equal(PlaybackState.Playing, _playbackService.State);
    }

    private async Task<bool> WaitForStateAsync(PlaybackState expected)
    {
        for (var i = 0; i < 100; i++)
        {
            if (_playbackService.State == expected) return true;
            await Task.Delay(10);
        }

        return false;
    }

    [Fact]
    public async Task Disconnect_ReturnsPromptly_AndDoesNotDeadlockTheHubThread()
    {
        Callback.OnScreenConnected("Screen 1", "conn-1");
        await StartPlayingAsync();

        // Runs on the hub's calling thread while the connection lock is held.
        var disconnect = Task.Run(() => Callback.OnScreenDisconnected("conn-1"));

        Assert.True(await Task.WhenAny(disconnect, Task.Delay(5000)) == disconnect,
            "OnScreenDisconnected blocked — the disconnect handler is querying screens on the hub thread.");

        // The lock must be free afterwards, which a deadlocked handler would prevent.
        Assert.False(await AnyScreensAsync().WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task LastScreenDisconnecting_PausesPlayback()
    {
        Callback.OnScreenConnected("Screen 1", "conn-1");
        await StartPlayingAsync();

        Callback.OnScreenDisconnected("conn-1");

        Assert.True(await WaitForStateAsync(PlaybackState.Paused));
    }

    [Fact]
    public async Task OneOfTwoScreensDisconnecting_KeepsPlaying()
    {
        Callback.OnScreenConnected("Screen 1", "conn-1");
        Callback.OnScreenConnected("Screen 2", "conn-2");
        await StartPlayingAsync();

        Callback.OnScreenDisconnected("conn-1");

        Assert.False(await WaitForStateAsync(PlaybackState.Paused));
        Assert.Equal(PlaybackState.Playing, _playbackService.State);
    }

    [Fact]
    public async Task PlayAsync_IsRefused_UntilAScreenConnects()
    {
        var performance = new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = Guid.NewGuid() };
        var media = new Media
        {
            Id = performance.MediaId,
            Title = "Song",
            Artist = "Artist",
            FilePath = "/library/song.mp4",
            Format = "mp4",
        };

        await _playbackService.LoadAsync(performance, media);
        await _playbackService.PlayAsync();

        Assert.Equal(PlaybackState.Stopped, _playbackService.State);

        Callback.OnScreenConnected("Screen 1", "conn-1");
        await _playbackService.PlayAsync();

        Assert.Equal(PlaybackState.Playing, _playbackService.State);
    }

    private async Task<bool> AnyScreensAsync()
    {
        await foreach (var _ in _screenServer.GetConnectedScreensAsync())
            return true;

        return false;
    }
}
