using KHost.Abstractions.Services.IPC;
using KHost.Cast;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Cast;

/// <summary>
/// The real CastScreenTransport behind the real CompositeScreenServer and the real coordination
/// service — the point being that nothing above the transport knows a Chromecast from a laptop.
/// Skips unless the emulator is listening.
/// </summary>
public class CastScreenCoordinationTests : IAsyncLifetime
{
    private const string CastName = "KHost Test Cast";

    private readonly CastScreenTransport _cast = new(
        NullLogger<CastScreenTransport>.Instance,
        Options.Create(new CastScreenTransport.ServiceOptions
        {
            Enabled = true,
            DiscoveryTimeout = TimeSpan.FromSeconds(5),
        }));

    private readonly FakeSyncTransport _local = new();

    private CompositeScreenServer _screens = null!;
    private ScreenCoordinationService _coordination = null!;

    public async Task InitializeAsync()
    {
        await _cast.InitializeAsync();

        _screens = new CompositeScreenServer([_local, _cast], NullLogger<CompositeScreenServer>.Instance);
        _coordination = new ScreenCoordinationService(NullLogger<ScreenCoordinationService>.Instance, _screens);
    }

    public Task DisposeAsync()
    {
        _coordination.Dispose();
        _screens.Dispose();
        _cast.Dispose();
        return Task.CompletedTask;
    }

    [RequiresCastEmulatorFact]
    public async Task ACastDeviceAlone_TakesTheAudioRole_AndLeavesTimingVacant()
    {
        await _cast.AttachAsync(CastName);

        Assert.Equal(CastName, await _coordination.EnsureRolesAsync());

        // Nothing present can be held to a schedule, so there is no timing reference to elect.
        Assert.Null(_coordination.TimingScreenId);
        Assert.True(_coordination.IsAudioEnabled(CastName));
    }

    [RequiresCastEmulatorFact]
    public async Task ALocalScreenPresentFirst_TakesAudioOverACastDevice()
    {
        _local.Add("Laptop", sync: true, audio: true);
        await _coordination.EnsureRolesAsync();

        await _cast.AttachAsync(CastName);
        await _coordination.EnsureRolesAsync();

        // Correcting a screen means seeking it, so audio stays where it can also anchor.
        Assert.Equal("Laptop", _coordination.AudioScreenId);
        Assert.Equal("Laptop", _coordination.TimingScreenId);
        Assert.False(_coordination.RolesAreSplit);
    }

    [RequiresCastEmulatorFact]
    public async Task ACastDeviceHoldingAudio_KeepsIt_WhenALocalScreenArrivesLater()
    {
        await _cast.AttachAsync(CastName);
        await _coordination.EnsureRolesAsync();
        Assert.Equal(CastName, _coordination.AudioScreenId);

        _local.Add("Laptop", sync: true, audio: true);
        await _coordination.EnsureRolesAsync();

        // The incumbent keeps the role even though the newcomer could unite audio and timing:
        // moving the room's audio out from under a song in progress is worse than a split, and
        // the screens page offers "Send audio here" for when the user actually wants it moved.
        Assert.Equal(CastName, _coordination.AudioScreenId);
        Assert.Equal("Laptop", _coordination.TimingScreenId);
        Assert.True(_coordination.RolesAreSplit);
    }

    [RequiresCastEmulatorFact]
    public async Task SendingAudioToTheCastDevice_SplitsTheRoles_AndMutesTheLocalScreen()
    {
        await _cast.AttachAsync(CastName);
        _local.Add("Laptop", sync: true, audio: true);
        await _coordination.EnsureRolesAsync();

        Assert.True(await _coordination.SetAudioScreenAsync(CastName));

        Assert.Equal(CastName, _coordination.AudioScreenId);
        Assert.Equal("Laptop", _coordination.TimingScreenId);
        Assert.True(_coordination.RolesAreSplit);

        // The mute has to have actually left the host — this is the whole point of the transport.
        Assert.Contains(_local.Sent, c => c.ScreenId == "Laptop"
            && c.Command is SetVolumeCommand { Volume: 0f });
    }

    [RequiresCastEmulatorFact]
    public async Task TheCastDeviceIsVisibleThroughTheScreenServer_LikeAnyOtherScreen()
    {
        await _cast.AttachAsync(CastName);
        _local.Add("Laptop", sync: true, audio: true);

        var screens = new List<IScreenConnection>();
        await foreach (var screen in _screens.GetConnectedScreensAsync()) screens.Add(screen);

        Assert.Contains(screens, s => s.ScreenId == "Laptop");
        Assert.Contains(screens, s => s.ScreenId == CastName && !s.Capabilities.SupportsSync);
    }

    [RequiresCastEmulatorFact]
    public async Task TheScreenServerRoutesToWhicheverTransportOwnsTheScreen()
    {
        await _cast.AttachAsync(CastName);
        _local.Add("Laptop", sync: true, audio: true);

        await _screens.SendCommandAsync(CastName, new PauseCommand());
        await _screens.SendCommandAsync("Laptop", new PauseCommand());

        // The Cast device's pause must not have been delivered to the SignalR transport.
        Assert.Single(_local.Sent, c => c.ScreenId == "Laptop" && c.Command is PauseCommand);
        Assert.DoesNotContain(_local.Sent, c => c.ScreenId == CastName);
    }

    /// <summary>Stands in for the SignalR transport without needing a hub.</summary>
    private sealed class FakeSyncTransport : IScreenTransport
    {
        private readonly List<IScreenConnection> _screens = [];

        public List<(string ScreenId, IScreenCommand Command)> Sent { get; } = [];

        public event EventHandler<ScreenConnectionEventArgs>? ScreenConnected;
        public event EventHandler<ScreenConnectionEventArgs>? ScreenDisconnected;
        public event EventHandler<ScreenStateReceivedEventArgs>? StateReceived;

        public void Add(string screenId, bool sync, bool audio)
        {
            var screen = Substitute.For<IScreenConnection>();
            screen.ScreenId.Returns(screenId);
            screen.ConnectionId.Returns($"conn-{screenId}");
            screen.IsConnected.Returns(true);
            screen.Capabilities.Returns(new ScreenCapabilities
            {
                SupportsSync = sync,
                SupportsAudio = audio,
                SupportsVideo = true,
            });

            _screens.Add(screen);
            ScreenConnected?.Invoke(this, new ScreenConnectionEventArgs { Connection = screen });
        }

        public async IAsyncEnumerable<IScreenConnection> GetConnectedScreensAsync()
        {
            foreach (var screen in _screens.ToList()) yield return screen;
            await Task.CompletedTask;
        }

        public Task<bool> SendCommandAsync(string screenId, IScreenCommand command)
        {
            if (_screens.All(s => s.ScreenId != screenId)) return Task.FromResult(false);

            Sent.Add((screenId, command));
            return Task.FromResult(true);
        }

        public void RaiseDisconnected(IScreenConnection screen)
            => ScreenDisconnected?.Invoke(this, new ScreenConnectionEventArgs { Connection = screen });

        public void RaiseState(ScreenStateReceivedEventArgs args) => StateReceived?.Invoke(this, args);
    }
}
