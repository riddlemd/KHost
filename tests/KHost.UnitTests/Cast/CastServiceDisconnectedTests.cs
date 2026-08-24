using KHost.Domain.Services.Messaging;
using KHost.Cast;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Cast;

/// <summary>
/// The paths a host hits with no receiver attached, which is most of a night. These reach no
/// network, so unlike <see cref="CastServiceTests"/> they run without the emulator.
/// </summary>
public class CastServiceDisconnectedTests : IDisposable
{
    private readonly CastService _cast = new(
        NullLogger<CastService>.Instance,
        Options.Create(new CastService.ServiceOptions()),
        new MessageBroker(NullLogger<MessageBroker>.Instance));

    [Fact]
    public void AFreshServiceIsIdleAndUnconnected()
    {
        Assert.Empty(_cast.Devices);
        Assert.Null(_cast.ConnectedDeviceId);
        Assert.False(_cast.IsDiscovering);
    }

    [Fact]
    public async Task ConnectAsync_IsRefused_WhenNothingHasBeenDiscovered()
    {
        Assert.False(await _cast.ConnectAsync("No Such TV"));
        Assert.Null(_cast.ConnectedDeviceId);
    }

    [Fact]
    public async Task Transport_IsSilentlyIgnored_WhenNothingIsConnected()
    {
        // A song plays whether or not anyone is casting, so none of these may throw.
        await _cast.LoadAsync("http://192.168.1.10:5251/media/abc/stream.m3u8", TimeSpan.Zero);
        await _cast.PlayAsync();
        await _cast.SeekAsync(TimeSpan.FromSeconds(10));
        await _cast.PauseAsync();
        await _cast.StopAsync();

        Assert.Null(_cast.ConnectedDeviceId);
    }

    [Fact]
    public async Task DisconnectAsync_IsHarmless_WhenNothingIsConnected()
    {
        await _cast.DisconnectAsync();

        Assert.Null(_cast.ConnectedDeviceId);
    }

    [Fact]
    public async Task StopDiscoveryAsync_IsHarmless_WhenDiscoveryNeverStarted()
    {
        await _cast.StopDiscoveryAsync();

        Assert.False(_cast.IsDiscovering);
        Assert.Empty(_cast.Devices);
    }

    [Fact]
    public async Task NoPlaybackStatusIsReported_WhileNothingIsConnected()
    {
        var reported = 0;
        _cast.PlaybackStatusChanged += (_, _) => reported++;

        await _cast.LoadAsync("http://192.168.1.10:5251/media/abc/stream.m3u8", TimeSpan.Zero);
        await _cast.PlayAsync();

        // The fallback clock must not tick from a receiver that was never there.
        Assert.Equal(0, reported);
    }

    public void Dispose() => _cast.Dispose();
}
