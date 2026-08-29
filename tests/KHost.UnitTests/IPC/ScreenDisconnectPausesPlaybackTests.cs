using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.IPC.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;

namespace KHost.UnitTests.IPC;

// Uses the real ScreenServerService: it raises ScreenDisconnected while holding the same
// non-reentrant lock GetConnectedScreensAsync waits on, so an inline handler deadlocks the hub.
public class ScreenDisconnectPausesPlaybackTests : IDisposable
{
    private readonly ScreenServerService _screenServer;
    private readonly FakeKeyStore _keys = new();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly PlaybackService _playbackService;

    public ScreenDisconnectPausesPlaybackTests()
    {
        var hubContext = Substitute.For<IHubContext<ScreenHub>>();
        var clients = Substitute.For<IHubClients>();
        clients.Client(Arg.Any<string>()).Returns(Substitute.For<ISingleClientProxy>());
        clients.All.Returns(Substitute.For<IClientProxy>());
        hubContext.Clients.Returns(clients);

        _screenServer = new ScreenServerService(hubContext, _keys);

        var venues = Substitute.For<IVenuesService>();
        venues.ReadSelectedVenueAsync().Returns(new Venue
        {
            Id = Guid.NewGuid(),
            Name = "Test Venue",
            Settings = new Venue.VenueSettings(),
        });

        // Playback needs a host stream now: without one a load throws rather than sending a
        // command no screen could act on.
        var mediaStreams = Substitute.For<IMediaStreamService>();
        mediaStreams
            .OpenAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => new MediaStreamSession
            {
                Id = "stream-1",
                SourcePath = call.ArgAt<string>(0),
                PlaylistUrl = "http://host/media/stream-1/stream.m3u8",
                StartOffset = call.ArgAt<TimeSpan>(1),
                Pitch = 0,
            });

        _playbackService = new PlaybackService(
            NullLogger<PlaybackService>.Instance,
            Substitute.For<ISingerQueueService>(),
            Substitute.For<IPerformanceService>(),
            venues,
            Substitute.For<IAnalyticsService>(),
            _screenServer,
            mediaStreams,
            new ScreenCoordinationService(NullLogger<ScreenCoordinationService>.Instance, _screenServer, Substitute.For<IVenuesService>(), _broker),
            Substitute.For<ICastService>(),
            Substitute.For<IBreakMusicService>(),
            Substitute.For<IMediaService>(),
            Options.Create(new PlaybackService.ServiceOptions { StopFadeDuration = TimeSpan.Zero }),
            _broker);
    }

    public void Dispose() => _playbackService.Dispose();

    private IHubCallback Callback => _screenServer;

    // A screen only exists once it has completed the signed handshake, so the tests register the
    // way the hub does: provision a key, take the session nonce, sign the registration.
    private void Register(string connectionId, string screenId)
    {
        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        _keys.Set(screenId, key);

        var nonce = Callback.BeginSession(connectionId);
        var payload = RegisterPayload.From(ScreenCapabilities.None).ToJson();
        var mac = ScreenMessageAuth.Sign(key, nonce, 1, payload);

        Callback.TryRegisterScreen(connectionId, null, new SignedEnvelope(screenId, 1, payload, mac).ToJson());
    }

    private sealed class FakeKeyStore : IScreenKeyStore
    {
        private readonly Dictionary<string, byte[]> _keys = [];
        public void Set(string screenId, byte[] key) => _keys[screenId] = key;
        public string Provision(string screenId) => $"/keys/{screenId}.key";
        public byte[]? GetKey(string screenId) => _keys.GetValueOrDefault(screenId);
        public void Revoke(string screenId) => _keys.Remove(screenId);
    }

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
            Status = MediaStatus.Ready,
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
        Register("conn-1", "Screen 1");
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
        Register("conn-1", "Screen 1");
        await StartPlayingAsync();

        Callback.OnScreenDisconnected("conn-1");

        Assert.True(await WaitForStateAsync(PlaybackState.Paused));
    }

    [Fact]
    public async Task OneOfTwoScreensDisconnecting_KeepsPlaying()
    {
        Register("conn-1", "Screen 1");
        Register("conn-2", "Screen 2");
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
            Status = MediaStatus.Ready,
        };

        await _playbackService.LoadAsync(performance, media);
        await _playbackService.PlayAsync();

        Assert.Equal(PlaybackState.Stopped, _playbackService.State);

        Register("conn-1", "Screen 1");
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
