using System.Reflection;
using System.Security.Cryptography;
using KHost.Abstractions.Services.IPC;
using KHost.IPC.SignalR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.IPC;

/// <summary>Exercises <see cref="ScreenClient"/> against a real loopback server. It hardcodes its own
/// <c>HubConnectionBuilder</c> with no seam for <c>TestServer</c>'s in-memory handler, so a live socket
/// on 127.0.0.1 is the only way to drive it end to end.</summary>
public sealed class ScreenClientTests : IAsyncLifetime
{
    private readonly FakeKeyStore _keys = new();
    private WebApplication? _app;
    private string _url = "";

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSignalR().AddJsonProtocol(o =>
            o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, ScreenCommandJsonContext.Default));
        builder.Services.AddOptions<ScreenServerService.ServiceOptions>();
        builder.Services.AddSingleton<IScreenKeyStore>(_keys);
        builder.Services.AddSingleton<ScreenServerService>();
        builder.Services.AddSingleton<IScreenServer>(sp => sp.GetRequiredService<ScreenServerService>());
        builder.Services.AddSingleton<IHubCallback>(sp => sp.GetRequiredService<ScreenServerService>());

        _app = builder.Build();
        _app.MapHub<ScreenHub>("/ipc/screen");
        await _app.StartAsync();

        var address = _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _url = $"{address}/ipc/screen";
    }

    public async Task DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
    }

    /// <summary>Regression test for the bug where <c>_key</c> was overwritten before the "Already
    /// connected" guard ran: a rejected second <c>ConnectAsync</c> must leave the live connection's
    /// key, and therefore its ability to sign messages the server accepts, untouched.</summary>
    [Fact]
    public async Task ConnectAsync_ARejectedSecondCall_LeavesTheFirstConnectionsKeyIntact()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        _keys.Set("Screen 1", key);

        await using var client = new ScreenClient(NullLoggerFactory.Instance);
        await client.ConnectAsync(_url, "Screen 1", authKey: key);
        Assert.Equal(ScreenClientState.Connected, client.State);

        // A second connect while already connected is refused — with a different, bogus key. If that
        // key ever reaches `_key`, everything signed afterwards is signed wrong. (The rejection itself
        // also flips State to Error through ConnectAsync's own catch block — unrelated to this bug —
        // so the proof reads `_key` directly rather than routing another message through State.)
        var bogusKey = RandomNumberGenerator.GetBytes(32);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ConnectAsync(_url, "Screen 1", authKey: bogusKey));

        var actualKey = (byte[]?)typeof(ScreenClient)
            .GetField("_key", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client);

        Assert.Equal(key, actualKey);
    }

    /// <summary>Regression test for the unobserved <c>_ = SendRegisterAsync()</c> fire-and-forget on
    /// reconnect: a failure there must not vanish silently while <c>State</c> keeps reporting
    /// Connected. Drives the private retry method directly — forcing a genuine registration failure
    /// through a live reconnect is not deterministic, but the method's own contract (observe, log,
    /// make State honest) is exactly what's under test.</summary>
    [Fact]
    public async Task ReregisterAfterReconnect_ObservesAndLogsAFailure_InsteadOfLeavingStateConnected()
    {
        await using var client = new ScreenClient(NullLoggerFactory.Instance);

        var states = new List<ScreenClientState>();
        client.StateChanged += (_, e) => states.Add(e.NewState);

        // A HubConnection that was never started: signing has nothing to sign with either, so
        // SendRegisterAsync throws before it ever touches the connection.
        var connection = new HubConnectionBuilder().WithUrl(_url).Build();
        typeof(ScreenClient).GetField("_connection", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(client, connection);

        var method = typeof(ScreenClient).GetMethod("ReregisterAfterReconnectAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(client, null)!;

        Assert.Equal(ScreenClientState.Error, client.State);
        Assert.Contains(ScreenClientState.Error, states);

        // No manual disposal of `connection` here: `client`'s own teardown (below, via `await using`)
        // owns it from this point and disposes it exactly once through DisconnectAsync.
    }

    private sealed class FakeKeyStore : IScreenKeyStore
    {
        private readonly Dictionary<string, byte[]> _keys = [];

        public void Set(string screenId, byte[] key) => _keys[screenId] = key;

        public string Provision(string screenId)
        {
            _keys[screenId] = RandomNumberGenerator.GetBytes(32);
            return $"/keys/{screenId}.key";
        }

        public byte[]? GetKey(string screenId) => _keys.GetValueOrDefault(screenId);

        public void Revoke(string screenId) => _keys.Remove(screenId);
    }
}
