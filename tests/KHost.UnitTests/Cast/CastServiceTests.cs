using System.Net.Sockets;
using System.Reflection;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Cast;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Cast;

public class CastServiceSeparationTests
{
    [Fact]
    public void CastService_IsNotAScreen()
    {
        var implemented = typeof(CastService).GetInterfaces();

        // The separation is the point: a receiver cannot be held to the group timeline, so it can
        // never be the primary and must not quietly inherit whatever the screens gain next. If it
        // ever implements a screen interface again, it is back in the role system by accident.
        Assert.DoesNotContain(typeof(IScreenServer), implemented);
        Assert.DoesNotContain(typeof(IScreenConnection), implemented);
        Assert.DoesNotContain(typeof(IScreenProvider), implemented);
    }

    [Fact]
    public void CastService_ExposesNoScreenCommandSurface()
    {
        var takesCommands = typeof(ICastService).GetMethods()
            .Where(m => m.GetParameters().Any(p => typeof(IScreenCommand).IsAssignableFrom(p.ParameterType)))
            .Select(m => m.Name);

        // Casting has its own small vocabulary. Accepting IScreenCommand would be the doorway
        // through which every future screen feature leaked onto a device that cannot honour it.
        Assert.True(!takesCommands.Any(), $"ICastService takes screen commands: {string.Join(", ", takesCommands)}");
    }
}

public class CastServiceUrlTests
{
    [Theory]
    [InlineData("http://localhost:5251/media/a/stream.m3u8", "192.168.1.10", "http://192.168.1.10:5251/media/a/stream.m3u8")]
    [InlineData("http://127.0.0.1:5251/media/a/stream.m3u8", "192.168.1.10", "http://192.168.1.10:5251/media/a/stream.m3u8")]
    public void MakeReachableFromDevice_ReplacesLoopback(string url, string lan, string expected)
    {
        // The host resolves its own base address to localhost, which on a television means the
        // television — the single most likely way for casting to silently fetch nothing.
        Assert.Equal(expected, CastService.MakeReachableFromDevice(url, lan));
    }

    [Fact]
    public void MakeReachableFromDevice_LeavesARoutableAddressAlone()
    {
        const string url = "http://192.168.1.5:5251/media/a/stream.m3u8";

        Assert.Equal(url, CastService.MakeReachableFromDevice(url, "192.168.1.10"));
    }

    [Fact]
    public void MakeReachableFromDevice_LeavesTheUrlAlone_WhenThereIsNoLanAddress()
        => Assert.Equal("http://localhost:5251/a.m3u8",
            CastService.MakeReachableFromDevice("http://localhost:5251/a.m3u8", null));
}

/// <summary>
/// Drives a real CASTV2 receiver. Skips unless the emulator from
/// <c>~/Developer/riddlemd/Chromecast-Emulator</c> is listening on 127.0.0.1:8009.
/// </summary>
public class CastServiceTests : IAsyncLifetime
{
    // Resolved from discovery rather than hard-coded: the emulator can be started under any
    // --name, and pinning one turns "a differently named emulator is running" into failures
    // instead of skips.
    private string DeviceName => _cast.Devices.FirstOrDefault()?.Name
        ?? throw new InvalidOperationException(
            "Port 8009 is listening but no Cast device was discovered — is mDNS blocked?");

    private readonly CastService _cast = new(
        NullLogger<CastService>.Instance,
        Options.Create(new CastService.ServiceOptions
        {
            Enabled = true,
            DiscoveryTimeout = TimeSpan.FromSeconds(5),
        }));

    public async Task InitializeAsync() => await _cast.InitializeAsync();

    public Task DisposeAsync()
    {
        _cast.Dispose();
        return Task.CompletedTask;
    }

    [RequiresCastEmulatorFact]
    public void Initialize_DiscoversTheReceiver() => Assert.NotEmpty(_cast.Devices);

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
    public async Task Connect_IsRefused_ForAnUnknownDevice()
        => Assert.False(await _cast.ConnectAsync("No Such TV"));

    [RequiresCastEmulatorFact]
    public async Task OnlyOneDeviceIsEverConnected()
    {
        await _cast.ConnectAsync(DeviceName);

        // The host has one song to play; a second receiver would be a second room hearing it out
        // of step. Connecting elsewhere replaces rather than adds.
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
        // A song plays whether or not anyone is casting, so these must be no-ops rather than
        // throwing into the middle of a performance.
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
        Assert.DoesNotContain(_cast.Devices, d => d.IsConnected);
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
