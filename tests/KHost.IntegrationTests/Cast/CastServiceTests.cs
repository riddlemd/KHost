using System.Net.Sockets;
using KHost.Cast;
using KHost.Domain.Services.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.IntegrationTests.Cast;

/// <summary>
/// Drives a real CASTV2 receiver. Skips unless the emulator from
/// <c>~/Developer/riddlemd/Chromecast-Emulator</c> is listening on 127.0.0.1:8009.
/// </summary>
public class CastServiceTests : IAsyncLifetime
{
    // The emulator can be started under any --name; pinning one turns that into failures.
    private string DeviceName => _cast.Devices.FirstOrDefault()?.Name
        ?? throw new InvalidOperationException(
            "Port 8009 is listening but no Cast device was discovered — is mDNS blocked?");

    private readonly CastService _cast = new(
        NullLogger<CastService>.Instance,
        Options.Create(new CastService.ServiceOptions
        {
            DiscoveryTimeout = TimeSpan.FromSeconds(5),
        }),
        new MessageBroker(NullLogger<MessageBroker>.Instance));

    public async Task InitializeAsync() => await _cast.StartDiscoveryAsync();

    public Task DisposeAsync()
    {
        _cast.Dispose();
        return Task.CompletedTask;
    }

    [RequiresCastEmulatorFact]
    public void StartDiscovery_DiscoversTheReceiver() => Assert.NotEmpty(_cast.Devices);

    [RequiresCastEmulatorFact]
    public async Task Connect_MarksTheDeviceConnected_WithoutMakingItAScreen()
    {
        Assert.True(await _cast.ConnectAsync(DeviceName));

        Assert.Equal(DeviceName, _cast.ConnectedDeviceId);
        Assert.Contains(_cast.Devices, d => d.Name == DeviceName && d.IsConnected);
    }

    [RequiresCastEmulatorFact]
    public async Task Connect_IsIdempotent()
    {
        Assert.True(await _cast.ConnectAsync(DeviceName));
        Assert.True(await _cast.ConnectAsync(DeviceName));

        Assert.Single(_cast.Devices, d => d.IsConnected);
    }

    [RequiresCastEmulatorFact]
    public async Task Connect_OpensASession()
    {
        await _cast.ConnectAsync(DeviceName);

        Assert.NotNull(_cast.SessionId);
    }

    [RequiresCastEmulatorFact]
    public async Task Connect_IsIdempotent_DownToTheSession()
    {
        await _cast.ConnectAsync(DeviceName);
        var session = _cast.SessionId;

        await _cast.ConnectAsync(DeviceName);

        // The second call launches nothing, so reporting a new session would have the caller
        // reload a receiver that is already playing the song.
        Assert.Equal(session, _cast.SessionId);
    }

    [RequiresCastEmulatorFact]
    public async Task Reconnecting_OpensADifferentSession()
    {
        await _cast.ConnectAsync(DeviceName);
        var first = _cast.SessionId;

        await _cast.DisconnectAsync();
        await _cast.ConnectAsync(DeviceName);

        // The app was launched again, so the receiver knows nothing — which is the whole reason
        // the id exists rather than a connected flag.
        Assert.NotNull(_cast.SessionId);
        Assert.NotEqual(first, _cast.SessionId);
    }

    [RequiresCastEmulatorFact]
    public async Task Connect_IsRefused_ForAnUnknownDevice()
        => Assert.False(await _cast.ConnectAsync("No Such TV"));

    [RequiresCastEmulatorFact]
    public async Task OnlyOneDeviceIsEverConnected()
    {
        await _cast.ConnectAsync(DeviceName);

        // Connecting elsewhere replaces rather than adds.
        Assert.Single(_cast.Devices, d => d.IsConnected);
    }

    [RequiresCastEmulatorFact]
    public async Task Transport_ReachesTheReceiver()
    {
        await _cast.ConnectAsync(DeviceName);

        await _cast.LoadAsync("http://192.168.1.10:5251/media/abc/stream.m3u8", TimeSpan.Zero);
        await _cast.PlayAsync();
        await _cast.SeekAsync(TimeSpan.FromSeconds(10));
        await _cast.PauseAsync();
        await _cast.StopAsync();

        Assert.Equal(DeviceName, _cast.ConnectedDeviceId);
    }

    [RequiresCastEmulatorFact]
    public async Task Transport_IsSilentlyIgnored_WhenNothingIsConnected()
    {
        // A song plays whether or not anyone is casting.
        await _cast.LoadAsync("http://192.168.1.10:5251/media/abc/stream.m3u8", TimeSpan.Zero);
        await _cast.PlayAsync();
        await _cast.StopAsync();

        Assert.Null(_cast.ConnectedDeviceId);
    }

    [RequiresCastEmulatorFact]
    public async Task Disconnect_LetsGoOfTheDevice()
    {
        await _cast.ConnectAsync(DeviceName);

        await _cast.DisconnectAsync();

        Assert.Null(_cast.ConnectedDeviceId);
        Assert.Null(_cast.SessionId);
        Assert.DoesNotContain(_cast.Devices, d => d.IsConnected);
    }
}

/// <summary>xUnit 2 cannot skip at runtime, so the decision is made in the constructor.</summary>
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
