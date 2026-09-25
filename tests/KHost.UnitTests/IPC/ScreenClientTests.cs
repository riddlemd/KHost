using System.Reflection;
using System.Security.Cryptography;
using KHost.IPC.SignalR.Contracts;
using KHost.IPC.SignalR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR;
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
    private readonly ConnectionContexts _contexts = new();
    private RecordingHubCallback? _callback;
    private WebApplication? _app;
    private string _url = "";

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSignalR(o => o.AddFilter(_contexts)).AddJsonProtocol(o =>
            o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, ScreenCommandJsonContext.Default));
        builder.Services.AddOptions<ScreenServerService.ServiceOptions>();
        builder.Services.AddSingleton<IScreenKeyStore>(_keys);
        builder.Services.AddSingleton<ScreenServerService>();
        builder.Services.AddSingleton<IScreenServer>(sp => sp.GetRequiredService<ScreenServerService>());
        builder.Services.AddSingleton<IHubCallback>(sp =>
            _callback = new RecordingHubCallback(sp.GetRequiredService<ScreenServerService>()));

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
    /// reconnect: a failure there must not leave the link up but unregistered, which never recovers.
    /// Drives the private method on a live client whose nonce is taken away, so the register fails
    /// before it is sent and nothing on the hub side closes the connection for it.</summary>
    [Fact]
    public async Task ReregisterAfterReconnect_AFailure_HandsTheLinkToTheReconnectLoop()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        _keys.Set("Screen 1", key);

        await using var client = new ScreenClient(NullLoggerFactory.Instance);
        await client.ConnectAsync(_url, "Screen 1", authKey: key);

        var server = _app!.Services.GetRequiredService<ScreenServerService>();
        var firstConnectionId = (await server.GetConnectedScreensAsync().SingleAsync()).ConnectionId;
        var backUp = WhenBackUp(client);

        typeof(ScreenClient).GetField("_nonce", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, null);
        var method = typeof(ScreenClient).GetMethod("ReregisterAfterReconnectAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(client, null)!;

        await backUp.WaitAsync(TimeSpan.FromSeconds(20));

        var registered = await server.GetConnectedScreensAsync().SingleAsync();
        Assert.NotEqual(firstConnectionId, registered.ConnectionId);
    }

    /// <summary>SignalR's own reconnect reports Reconnected before the screen has re-registered on
    /// the new connection. State sent in that gap is refused, and the hub drops the link over it.</summary>
    [Fact]
    public async Task AutomaticReconnect_SendsNoStateTheHostRefuses_AndReportsConnectedOnlyOnceRegistered()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        _keys.Set("Screen 1", key);

        await using var client = new ScreenClient(NullLoggerFactory.Instance);
        await client.ConnectAsync(_url, "Screen 1", authKey: key);

        var server = _app!.Services.GetRequiredService<ScreenServerService>();
        var firstConnectionId = (await server.GetConnectedScreensAsync().SingleAsync()).ConnectionId!;

        // Read at the moment Connected is reported, before anything else can register the screen.
        var registeredWhenConnected = new List<string?>();
        var sawReconnecting = false;
        var backUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StateChanged += (_, e) =>
        {
            if (e.NewState == ScreenClientState.Reconnecting) sawReconnecting = true;
            if (e.NewState != ScreenClientState.Connected || !sawReconnecting) return;

            registeredWhenConnected.Add(server.GetConnectedScreensAsync().ToBlockingEnumerable().SingleOrDefault()?.ConnectionId);
            backUp.TrySetResult();
        };

        // The ticker, standing in: state goes out continuously across the drop and the reconnect.
        using var stop = new CancellationTokenSource();
        var pump = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try { await client.SendStateAsync(PlaybackState()); }
                catch (Exception) { }

                await Task.Yield();
            }
        });

        _callback!.RegisterDelay = TimeSpan.FromMilliseconds(300);

        // A transport that dies without a close message is what SignalR's own reconnect answers.
        _contexts.Get(firstConnectionId).GetHttpContext()!.Abort();

        await backUp.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var acceptedAtReconnect = _callback.StateSeqs().Accepted.Count;
        await Task.Delay(200);
        stop.Cancel();
        await pump;

        var (accepted, refused) = _callback.StateSeqs();
        Assert.Empty(refused);
        Assert.True(accepted.Count > acceptedAtReconnect, "no state reached the host after the reconnect");

        var reconnectedId = Assert.Single(registeredWhenConnected);
        Assert.NotNull(reconnectedId);
        Assert.NotEqual(firstConnectionId, reconnectedId);
    }

    private static Task WhenBackUp(ScreenClient client)
    {
        var sawReconnecting = false;
        var backUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StateChanged += (_, e) =>
        {
            if (e.NewState == ScreenClientState.Reconnecting) sawReconnecting = true;
            else if (e.NewState == ScreenClientState.Connected && sawReconnecting) backUp.TrySetResult();
        };

        return backUp.Task;
    }

    /// <summary>Every hub connection's caller context, so a test can cut one at the transport.</summary>
    private sealed class ConnectionContexts : IHubFilter
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, HubCallerContext> _contexts = new();

        public HubCallerContext Get(string connectionId) => _contexts[connectionId];

        public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
        {
            _contexts[context.Context.ConnectionId] = context.Context;
            return next(context);
        }
    }

    /// <summary>The 1s ticker, the reply after each command and the ended events all send at once.
    /// Numbered before the send and not held to it, two of them reached the hub out of order, and
    /// the hub dropped the link for a sequence that did not advance.</summary>
    [Fact]
    public async Task SendStateAsync_ManySendsAtOnce_ReachTheHostInTheOrderTheyWereNumbered()
    {
        const int sends = 200;
        var key = RandomNumberGenerator.GetBytes(32);
        _keys.Set("Screen 1", key);

        await using var client = new ScreenClient(NullLoggerFactory.Instance);
        await client.ConnectAsync(_url, "Screen 1", authKey: key);

        // Released together so the sends contend for the wire rather than trickling in one by one.
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, sends).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            await client.SendStateAsync(PlaybackState());
        })).ToList();

        start.SetResult();
        await Task.WhenAll(tasks);

        var (accepted, refused) = _callback!.StateSeqs();
        Assert.Empty(refused);
        Assert.Equal(sends, accepted.Count);
        Assert.Equal(accepted.Order(), accepted);
        Assert.Equal(ScreenClientState.Connected, client.State);
    }

    /// <summary>A hub-side Abort closes without allowing SignalR's own reconnect, which used to leave
    /// the screen on "Lost the host" until someone relaunched it.</summary>
    [Fact]
    public async Task ConnectionAbortedByTheHost_IsWonBackAndRegisteredAgain()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        _keys.Set("Screen 1", key);

        await using var client = new ScreenClient(NullLoggerFactory.Instance);
        await client.ConnectAsync(_url, "Screen 1", authKey: key);

        var server = _app!.Services.GetRequiredService<ScreenServerService>();
        var firstConnectionId = (await server.GetConnectedScreensAsync().SingleAsync()).ConnectionId;

        var sawReconnecting = false;
        var backUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StateChanged += (_, e) =>
        {
            if (e.NewState == ScreenClientState.Reconnecting) sawReconnecting = true;
            else if (e.NewState == ScreenClientState.Connected && sawReconnecting) backUp.TrySetResult();
        };

        // A state report the hub cannot read is refused, and the hub aborts the connection.
        var connection = (HubConnection)typeof(ScreenClient)
            .GetField("_connection", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!;
        await connection.SendAsync(nameof(ScreenHub.ReceiveStateAsync), "not an envelope");

        await backUp.Task.WaitAsync(TimeSpan.FromSeconds(20));

        var registered = await server.GetConnectedScreensAsync().SingleAsync();
        Assert.Equal("Screen 1", registered.ScreenId);
        Assert.NotEqual(firstConnectionId, registered.ConnectionId);

        // Signed against the new session's nonce and numbering, so the host takes it.
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.StateReceived += (_, _) => received.TrySetResult();
        await client.SendStateAsync(PlaybackState());
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>A refusal must reach the client as a failed attempt: taken as a success, a screen turned
    /// away by the cap would reconnect at the fastest backoff all night.</summary>
    [Fact]
    public async Task ConnectAsync_RefusedByTheScreenCap_Throws()
    {
        var first = RandomNumberGenerator.GetBytes(32);
        var second = RandomNumberGenerator.GetBytes(32);
        _keys.Set("Screen 1", first);
        _keys.Set("Screen 2", second);

        await using var holder = new ScreenClient(NullLoggerFactory.Instance);
        await holder.ConnectAsync(_url, "Screen 1", authKey: first);

        await using var refused = new ScreenClient(NullLoggerFactory.Instance);
        await Assert.ThrowsAnyAsync<Exception>(() => refused.ConnectAsync(_url, "Screen 2", authKey: second));

        Assert.Equal(ScreenClientState.Connected, holder.State);
    }

    /// <summary>Without this, the exception <see cref="ConnectAsync_RefusedByTheScreenCap_Throws"/>
    /// asserts on is indistinguishable from the host simply never answering — which is the bug: a
    /// refused screen logged "could not reach the IPC server" and retried all night believing it had
    /// a network problem, when the host had answered and said no.</summary>
    [Fact]
    public async Task ConnectAsync_RefusedByTheScreenCap_NamesTheReasonInsteadOfLookingUnreachable()
    {
        var first = RandomNumberGenerator.GetBytes(32);
        var second = RandomNumberGenerator.GetBytes(32);
        _keys.Set("Screen 1", first);
        _keys.Set("Screen 2", second);

        await using var holder = new ScreenClient(NullLoggerFactory.Instance);
        await holder.ConnectAsync(_url, "Screen 1", authKey: first);

        await using var refused = new ScreenClient(NullLoggerFactory.Instance);
        await Assert.ThrowsAnyAsync<Exception>(() => refused.ConnectAsync(_url, "Screen 2", authKey: second));

        Assert.NotNull(refused.LastRefusalReason);
        Assert.Contains("already registered", refused.LastRefusalReason);
    }

    private static ScreenPlaybackState PlaybackState() => new()
    {
        StreamUrl = null,
        IsPlaying = false,
        Position = TimeSpan.Zero,
        Duration = TimeSpan.Zero,
    };

    /// <summary>The real callback, with the sequence of every state report it judged, in arrival order.</summary>
    private sealed class RecordingHubCallback(IHubCallback inner) : IHubCallback
    {
        private readonly Lock _lock = new();
        private readonly List<long> _accepted = [];
        private readonly List<long> _refused = [];

        /// <summary>Holds each registration, so the client sees its transport back well before the register lands.</summary>
        public TimeSpan RegisterDelay { get; set; }

        public (List<long> Accepted, List<long> Refused) StateSeqs()
        {
            lock (_lock) { return ([.. _accepted], [.. _refused]); }
        }

        public bool TryAcceptState(string connectionId, string envelopeJson)
        {
            var accepted = inner.TryAcceptState(connectionId, envelopeJson);

            if (SignedEnvelope.TryParse(envelopeJson) is { } envelope)
                lock (_lock) { (accepted ? _accepted : _refused).Add(envelope.Seq); }

            return accepted;
        }

        public void OnScreenDisconnected(string connectionId) => inner.OnScreenDisconnected(connectionId);
        public bool TryAcquireConnectionSlot(string connectionId) => inner.TryAcquireConnectionSlot(connectionId);
        public void ReleaseConnectionSlot(string connectionId) => inner.ReleaseConnectionSlot(connectionId);
        public string BeginSession(string connectionId) => inner.BeginSession(connectionId);
        public string? TryRegisterScreen(string connectionId, string? hostAddress, string envelopeJson)
        {
            if (RegisterDelay > TimeSpan.Zero) Thread.Sleep(RegisterDelay);
            return inner.TryRegisterScreen(connectionId, hostAddress, envelopeJson);
        }
        public bool IsAuthenticated(string connectionId) => inner.IsAuthenticated(connectionId);
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
