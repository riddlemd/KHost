using System.Net.Sockets;
using KHost.Abstractions.Services.IPC;
using KHost.Cast;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Cast;

public class CastScreenTransportUrlTests
{
    [Theory]
    [InlineData("http://localhost:5251/media/a/stream.m3u8", "192.168.1.10", "http://192.168.1.10:5251/media/a/stream.m3u8")]
    [InlineData("http://127.0.0.1:5251/media/a/stream.m3u8", "192.168.1.10", "http://192.168.1.10:5251/media/a/stream.m3u8")]
    public void MakeReachableFromDevice_ReplacesLoopback(string url, string lan, string expected)
    {
        // The host resolves its own base address to localhost, which on a television means the
        // television — the single most likely way for casting to silently fetch nothing.
        Assert.Equal(expected, CastScreenTransport.MakeReachableFromDevice(url, lan));
    }

    [Fact]
    public void MakeReachableFromDevice_LeavesARoutableAddressAlone()
    {
        const string url = "http://192.168.1.5:5251/media/a/stream.m3u8";

        // A configured address is deliberate and must survive.
        Assert.Equal(url, CastScreenTransport.MakeReachableFromDevice(url, "192.168.1.10"));
    }

    [Fact]
    public void MakeReachableFromDevice_LeavesTheUrlAlone_WhenThereIsNoLanAddress()
    {
        const string url = "http://localhost:5251/media/a/stream.m3u8";

        Assert.Equal(url, CastScreenTransport.MakeReachableFromDevice(url, null));
    }
}

/// <summary>
/// Drives a real CASTV2 receiver. Skips unless the emulator from
/// <c>~/Developer/riddlemd/Chromecast-Emulator</c> is listening on 127.0.0.1:8009.
/// </summary>
public class CastScreenTransportTests : IAsyncLifetime
{
    // Resolved from discovery rather than hard-coded: the emulator can be started under any
    // --name, and pinning one turned "a differently named emulator is running" into ten confusing
    // failures instead of a skip.
    private string DeviceName => _transport.Devices.FirstOrDefault()?.Name
        ?? throw new InvalidOperationException(
            "Port 8009 is listening but no Cast device was discovered — is mDNS blocked?");

    private readonly CastScreenTransport _transport = new(
        NullLogger<CastScreenTransport>.Instance,
        Options.Create(new CastScreenTransport.ServiceOptions
        {
            Enabled = true,
            DiscoveryTimeout = TimeSpan.FromSeconds(5),
        }));

    public async Task InitializeAsync() => await _transport.InitializeAsync();

    public Task DisposeAsync()
    {
        _transport.Dispose();
        return Task.CompletedTask;
    }

    [RequiresCastEmulatorFact]
    public void Initialize_DiscoversTheReceiver()
        => Assert.Contains(_transport.Devices, d => d.Name == DeviceName);

    [RequiresCastEmulatorFact]
    public async Task Attach_PublishesTheDeviceAsAnUnsyncableScreen()
    {
        Assert.True(await _transport.AttachAsync(DeviceName));

        var screen = await SingleScreenAsync();

        Assert.Equal(DeviceName, screen.ScreenId);

        // The whole reason the roles were split: a Cast device carries audio but can never be
        // held to the group's timeline.
        Assert.False(screen.Capabilities.SupportsSync);
        Assert.True(screen.Capabilities.SupportsAudio);
    }

    [RequiresCastEmulatorFact]
    public async Task SendCommand_ReturnsFalse_ForAScreenOnAnotherTransport()
    {
        await _transport.AttachAsync(DeviceName);

        // False rather than throwing is what lets the screen server try the next transport.
        Assert.False(await _transport.SendCommandAsync("Some SignalR Screen", new PauseCommand()));
    }

    [RequiresCastEmulatorFact]
    public async Task LoadAndPlay_ReachTheReceiver_AndItReportsItsPosition()
    {
        await _transport.AttachAsync(DeviceName);

        ScreenPlaybackState? reported = null;
        _transport.StateReceived += (_, e) => reported = e.State as ScreenPlaybackState;

        Assert.True(await _transport.SendCommandAsync(DeviceName, new LoadMediaCommand
        {
            FilePath = "/library/song.mp4",
            StreamUrl = "http://192.168.1.10:5251/media/abc/stream.m3u8",
        }));

        Assert.True(await _transport.SendCommandAsync(DeviceName, new PlayCommand()));

        await WaitUntilAsync(() => reported?.IsPlaying == true);

        Assert.NotNull(reported);
        Assert.True(reported.IsPlaying);

        // Null on purpose: a Cast device is never the primary, and offering a sample time
        // would invite the host to anchor the whole group on a report it cannot trust.
        Assert.Null(reported.SampledAtUtc);
    }

    [RequiresCastEmulatorFact]
    public async Task Load_IsRefused_WithoutAStreamUrl()
    {
        await _transport.AttachAsync(DeviceName);

        // A file path is useless to a device with no access to the host's disk. The command is
        // still "ours" — it is accepted and dropped, not passed to another transport.
        Assert.True(await _transport.SendCommandAsync(DeviceName, new LoadMediaCommand
        {
            FilePath = "/library/song.mp4",
        }));
    }

    [RequiresCastEmulatorFact]
    public async Task SetVolumeZero_MutesTheReceiver()
    {
        await _transport.AttachAsync(DeviceName);

        // This is the auto-mute path for a Cast device.
        Assert.True(await _transport.SendCommandAsync(DeviceName, new SetVolumeCommand { Volume = 0f }));
    }

    [RequiresCastEmulatorFact]
    public async Task Detach_RemovesTheScreen()
    {
        await _transport.AttachAsync(DeviceName);
        Assert.NotEmpty(await ScreensAsync());

        await _transport.DetachAsync(DeviceName);

        Assert.Empty(await ScreensAsync());
    }

    private async Task<IScreenConnection> SingleScreenAsync() => Assert.Single(await ScreensAsync());

    private async Task<List<IScreenConnection>> ScreensAsync()
    {
        var screens = new List<IScreenConnection>();
        await foreach (var screen in _transport.GetConnectedScreensAsync()) screens.Add(screen);
        return screens;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(50);
    }
}

/// <summary>
/// xUnit 2 cannot skip at runtime from inside a test, so the decision is made while the attribute
/// is constructed.
/// </summary>
public sealed class RequiresCastEmulatorFactAttribute : FactAttribute
{
    public RequiresCastEmulatorFactAttribute()
    {
        if (!EmulatorIsListening.Value)
            Skip = "the Chromecast emulator is not listening on 127.0.0.1:8009";
    }

    private static readonly Lazy<bool> EmulatorIsListening = new(() =>
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync("127.0.0.1", 8009).Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            return false;
        }
    });
}
