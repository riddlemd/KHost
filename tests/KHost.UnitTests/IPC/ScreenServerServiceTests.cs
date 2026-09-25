using System.Security.Cryptography;
using KHost.IPC.SignalR.Contracts;
using KHost.IPC.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.IPC;

public class ScreenServerServiceTests
{
    private readonly IHubContext<ScreenHub> _hubContext = Substitute.For<IHubContext<ScreenHub>>();
    private readonly IHubClients _clients = Substitute.For<IHubClients>();
    private readonly ISingleClientProxy _singleClient = Substitute.For<ISingleClientProxy>();
    private readonly FakeKeyStore _keys = new();
    private readonly Dictionary<string, string> _nonces = [];
    private readonly ScreenServerService _service;

    public ScreenServerServiceTests()
    {
        _clients.Client(Arg.Any<string>()).Returns(_singleClient);
        _hubContext.Clients.Returns(_clients);

        _service = new ScreenServerService(_hubContext, _keys);
    }

    private IHubCallback Callback => _service;

    private byte[] KeyFor(string screenId)
    {
        if (_keys.GetKey(screenId) is not { } key)
        {
            key = RandomNumberGenerator.GetBytes(32);
            _keys.Set(screenId, key);
        }

        return key;
    }

    private string Nonce(string connectionId, ScreenServerService? on = null)
    {
        // Keyed per connection, not per service: a session belongs to the server that begun it, so
        // a nonce cached from the default fixture would not verify against another one.
        var key = on is null ? connectionId : $"{on.GetHashCode()}:{connectionId}";

        if (!_nonces.TryGetValue(key, out var nonce))
            _nonces[key] = nonce = ((IHubCallback)(on ?? _service)).BeginSession(connectionId);

        return nonce;
    }

    private bool Register(
        string connectionId, string screenId, string? hostAddress = null,
        ScreenCapabilities? capabilities = null, long seq = 1, byte[]? signWith = null, string? nonceOverride = null,
        ScreenServerService? on = null)
    {
        IHubCallback callback = on ?? _service;
        var nonce = nonceOverride ?? Nonce(connectionId, on);
        var payload = RegisterPayload.From(capabilities ?? ScreenCapabilities.None).ToJson();
        var mac = ScreenMessageAuth.Sign(signWith ?? KeyFor(screenId), nonce, seq, payload);

        return callback.TryRegisterScreen(connectionId, hostAddress, new SignedEnvelope(screenId, seq, payload, mac).ToJson());
    }

    private bool SendState(string connectionId, string screenId, IScreenState state, long seq, byte[]? signWith = null)
    {
        var nonce = Nonce(connectionId);
        var payload = ScreenIpcSerializer.SerializeState(state);
        var mac = ScreenMessageAuth.Sign(signWith ?? KeyFor(screenId), nonce, seq, payload);

        return Callback.TryAcceptState(connectionId, new SignedEnvelope(screenId, seq, payload, mac).ToJson());
    }

    private async Task<List<IScreenConnection>> ConnectedScreensAsync(ScreenServerService? service = null)
    {
        var list = new List<IScreenConnection>();
        await foreach (var conn in (service ?? _service).GetConnectedScreensAsync())
            list.Add(conn);
        return list;
    }

    private ScreenServerService ServiceWithOptions(ScreenServerService.ServiceOptions options) =>
        new(_hubContext, _keys, Options.Create(options));

    /// <summary>The inner command/state payload of the last envelope sent to a client proxy.</summary>
    private static string? InnerPayload(string? envelopeJson)
        => envelopeJson is null ? null : SignedEnvelope.TryParse(envelopeJson)?.Payload;

    [Fact]
    public async Task GetConnectedScreensAsync_IsEmpty_Initially()
        => Assert.Empty(await ConnectedScreensAsync());

    [Fact]
    public async Task Register_TracksTheConnection()
    {
        Assert.True(Register("conn-a", "Screen 1"));

        var only = Assert.Single(await ConnectedScreensAsync());
        Assert.Equal("Screen 1", only.ScreenId);
        Assert.Equal("conn-a", only.ConnectionId);
        Assert.True(only.IsConnected);
    }

    [Fact]
    public void Register_RaisesScreenConnected()
    {
        ScreenConnectionEventArgs? captured = null;
        _service.ScreenConnected += (_, e) => captured = e;

        Register("conn-a", "Screen 1");

        Assert.NotNull(captured);
        Assert.Equal("Screen 1", captured.Connection.ScreenId);
    }

    [Fact]
    public async Task Register_CarriesTheDeclaredCapabilities()
    {
        Register("conn-a", "Screen 1", capabilities: new ScreenCapabilities { SupportsAudio = true });

        var only = Assert.Single(await ConnectedScreensAsync());
        Assert.True(only.Capabilities.SupportsAudio);
        Assert.False(only.Capabilities.SupportsVideo);
    }

    [Fact]
    public async Task Register_WithDuplicateScreenId_CollapsesToOneConnection()
    {
        Register("conn-a", "Screen");
        Register("conn-b", "Screen");

        var only = Assert.Single(await ConnectedScreensAsync());
        Assert.Equal("conn-b", only.ConnectionId);
    }

    [Fact]
    public async Task Register_WithNoProvisionedKey_IsRefused()
    {
        var nonce = Nonce("conn-a");
        var payload = RegisterPayload.From(ScreenCapabilities.None).ToJson();
        // A key nobody put in the store: the screen id has none, so the registration cannot be trusted.
        var mac = ScreenMessageAuth.Sign(RandomNumberGenerator.GetBytes(32), nonce, 1, payload);

        Assert.False(Callback.TryRegisterScreen("conn-a", null, new SignedEnvelope("stranger", 1, payload, mac).ToJson()));
        Assert.Empty(await ConnectedScreensAsync());
    }

    [Fact]
    public async Task Register_SignedWithTheWrongKey_IsRefused()
    {
        KeyFor("Screen 1");
        Assert.False(Register("conn-a", "Screen 1", signWith: RandomNumberGenerator.GetBytes(32)));
        Assert.Empty(await ConnectedScreensAsync());
    }

    [Fact]
    public async Task Register_ForAnUnprovisionedScreen_IsRefused_EvenSignedWithAnAllZeroKey()
    {
        // Substituting a default key for a missing one would let anyone in by signing with that
        // default; the id must have a real provisioned key, full stop.
        Assert.False(Register("conn-a", "stranger", signWith: new byte[32]));
        Assert.Empty(await ConnectedScreensAsync());
    }

    [Fact]
    public void Register_WithoutABegunSession_IsRefused()
    {
        var payload = RegisterPayload.From(ScreenCapabilities.None).ToJson();
        var mac = ScreenMessageAuth.Sign(KeyFor("Screen 1"), "some-nonce", 1, payload);

        // No BeginSession for this connection id, so there is no nonce to have signed against.
        Assert.False(Callback.TryRegisterScreen("never-began", null, new SignedEnvelope("Screen 1", 1, payload, mac).ToJson()));
    }

    [Fact]
    public void IsAuthenticated_IsFalseBeforeRegister_AndTrueAfter()
    {
        Nonce("conn-a");
        Assert.False(Callback.IsAuthenticated("conn-a"));

        Register("conn-a", "Screen 1");
        Assert.True(Callback.IsAuthenticated("conn-a"));
    }

    [Fact]
    public void TryAcceptState_FromAnUnauthenticatedConnection_IsRefused()
    {
        Nonce("conn-a"); // session begun but never registered
        Assert.False(SendState("conn-a", "Screen 1", Playing(), seq: 1));
    }

    [Fact]
    public void TryAcceptState_FromAnUnauthenticatedConnection_IsRefused_WithoutReachingVerification()
    {
        var nonce = Nonce("conn-a"); // begun, never registered: no key, no screen id

        // A crafted envelope whose id matches the unregistered session's (null): the key check has
        // to refuse it before verification runs against a null key.
        var payload = ScreenIpcSerializer.SerializeState(Playing());
        var envelope = new SignedEnvelope(null!, 1, payload, "any-mac");

        Assert.False(Callback.TryAcceptState("conn-a", envelope.ToJson()));
    }

    [Fact]
    public void TryAcceptState_RaisesStateReceived_WhenSignedAndInOrder()
    {
        Register("conn-a", "Screen 1", seq: 1);

        ScreenStateReceivedEventArgs? captured = null;
        _service.StateReceived += (_, e) => captured = e;

        Assert.True(SendState("conn-a", "Screen 1", Playing(), seq: 2));

        Assert.NotNull(captured);
        Assert.Equal("Screen 1", captured.ScreenId);
    }

    [Fact]
    public void TryAcceptState_WithAStaleSequence_IsRefused()
    {
        Register("conn-a", "Screen 1", seq: 1);
        Assert.True(SendState("conn-a", "Screen 1", Playing(), seq: 2));

        // seq must advance: replaying seq 2 (or anything <= it) is a replay.
        Assert.False(SendState("conn-a", "Screen 1", Playing(), seq: 2));
    }

    [Fact]
    public void TryAcceptState_SignedWithTheWrongKey_IsRefused()
    {
        Register("conn-a", "Screen 1", seq: 1);
        Assert.False(SendState("conn-a", "Screen 1", Playing(), seq: 2, signWith: RandomNumberGenerator.GetBytes(32)));
    }

    [Fact]
    public async Task OnScreenDisconnected_RemovesTheMatchingConnection()
    {
        Register("conn-a", "Screen 1");

        Callback.OnScreenDisconnected("conn-a");

        Assert.Empty(await ConnectedScreensAsync());
    }

    [Fact]
    public async Task OnScreenDisconnected_ForAnotherConnection_LeavesTheScreenTracked()
    {
        Register("conn-a", "Screen 1");

        Callback.OnScreenDisconnected("conn-unregistered");

        Assert.Equal("Screen 1", Assert.Single(await ConnectedScreensAsync()).ScreenId);
    }

    [Fact]
    public void OnScreenDisconnected_RaisesScreenDisconnected()
    {
        Register("conn-a", "Screen 1");

        ScreenConnectionEventArgs? captured = null;
        _service.ScreenDisconnected += (_, e) => captured = e;

        Callback.OnScreenDisconnected("conn-a");

        Assert.NotNull(captured);
        Assert.Equal("Screen 1", captured.Connection.ScreenId);
    }

    [Fact]
    public async Task OnScreenDisconnected_ForStaleConnectionId_LeavesReconnectedScreenTracked()
    {
        Register("conn-old", "Screen 1");
        Register("conn-new", "Screen 1");

        Callback.OnScreenDisconnected("conn-old");

        var only = Assert.Single(await ConnectedScreensAsync());
        Assert.Equal("conn-new", only.ConnectionId);
    }

    [Fact]
    public void OnScreenDisconnected_EndsTheSession_SoAReplayCannotContinue()
    {
        Register("conn-a", "Screen 1");
        Callback.OnScreenDisconnected("conn-a");

        Assert.False(Callback.IsAuthenticated("conn-a"));
    }

    [Fact]
    public async Task BroadcastCommandAsync_SendsASignedCommandToTheScreensConnection()
    {
        Register("conn-a", "Screen 1");

        string? sent = null;
        await _singleClient.SendCoreAsync(
            Arg.Any<string>(), Arg.Do<object?[]>(a => sent = a[0] as string), Arg.Any<CancellationToken>());

        await _service.BroadcastCommandAsync(new PlayCommand());

        _clients.Received().Client("conn-a");

        var envelope = SignedEnvelope.TryParse(sent!);
        Assert.NotNull(envelope);
        Assert.Equal("Screen 1", envelope!.ScreenId);
        // The command is inside, and the MAC verifies against the screen's key and session nonce.
        Assert.True(ScreenMessageAuth.Verify(KeyFor("Screen 1"), Nonce("conn-a"), envelope.Seq, envelope.Payload, envelope.Mac));
        Assert.Contains("$type", envelope.Payload);
    }

    [Fact]
    public async Task BroadcastCommandAsync_IsANoOp_WithNoScreenRegistered()
    {
        await _service.BroadcastCommandAsync(new PlayCommand());

        await _singleClient.DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BroadcastCommandAsync_DoesNotSend_AfterTheScreenDisconnects()
    {
        Register("conn-a", "Screen 1");
        Callback.OnScreenDisconnected("conn-a");

        await _service.BroadcastCommandAsync(new PlayCommand());

        await _singleClient.DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BroadcastCommandAsync_GivesAScreenAcrossTheNetworkAStreamUrlItCanReach()
    {
        Register("conn-across", "Across", hostAddress: "192.168.0.99");

        string? sent = null;
        await _singleClient.SendCoreAsync(
            Arg.Any<string>(), Arg.Do<object?[]>(a => sent = a[0] as string), Arg.Any<CancellationToken>());

        await _service.BroadcastCommandAsync(new LoadMediaCommand
        {
            StreamUrl = "http://localhost:5251/media/abc/stream.m3u8",
        });

        Assert.Contains("192.168.0.99:5251", InnerPayload(sent));
        Assert.DoesNotContain("localhost:5251", InnerPayload(sent));
    }

    [Fact]
    public async Task BroadcastCommandAsync_LeavesALocalScreensStreamUrlAlone()
    {
        Register("conn-here", "Here", hostAddress: "127.0.0.1");

        string? sent = null;
        await _singleClient.SendCoreAsync(
            Arg.Any<string>(), Arg.Do<object?[]>(a => sent = a[0] as string), Arg.Any<CancellationToken>());

        await _service.BroadcastCommandAsync(new LoadMediaCommand
        {
            StreamUrl = "http://localhost:5251/media/abc/stream.m3u8",
        });

        Assert.Contains("localhost:5251", InnerPayload(sent));
    }

    [Fact]
    public async Task BroadcastCommandAsync_SendsToTheScreensOwnConnection_EvenWithNoUrlToRewrite()
    {
        // Never Clients.All: the command carries a MAC under the screen's own key, so even one with
        // nothing screen-specific in it goes to that connection alone.
        Register("conn-a", "Screen 1");

        await _service.BroadcastCommandAsync(new PlayCommand());

        await _singleClient.Received(1).SendCoreAsync(
            "ReceiveCommand", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BroadcastCommandAsync_SerializesTheCommandThroughItsBaseType()
    {
        Register("conn-a", "Screen 1");

        string? sent = null;
        await _singleClient.SendCoreAsync(
            Arg.Any<string>(), Arg.Do<object?[]>(a => sent = a[0] as string), Arg.Any<CancellationToken>());

        await _service.BroadcastCommandAsync(new SeekCommand { Position = TimeSpan.FromSeconds(30) });

        var back = System.Text.Json.JsonSerializer.Deserialize<ScreenCommandBase>(
            InnerPayload(sent)!, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.IsType<SeekCommand>(back).Position);
    }

    // One display at a time: a hand-launched second screen must not simply join.
    [Fact]
    public async Task Register_ASecondScreen_IsRefused_AndDoesNotRaiseScreenConnected()
    {
        var service = new ScreenServerService(_hubContext, _keys);
        var raised = 0;
        service.ScreenConnected += (_, _) => raised++;

        Assert.True(RegisterOn(service, "conn-a", "Screen 1"));
        Assert.False(RegisterOn(service, "conn-b", "Screen 2"));

        var only = Assert.Single(await ConnectedScreensAsync(service));
        Assert.Equal("Screen 1", only.ScreenId);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Register_ReRegistrationOfTheSameId_StillOverwrites()
    {
        RegisterOn(_service, "conn-a", "Screen 1");
        Assert.True(RegisterOn(_service, "conn-b", "Screen 1"));

        var only = Assert.Single(await ConnectedScreensAsync());
        Assert.Equal("conn-b", only.ConnectionId);
    }

    [Fact]
    public void TryAcquireConnectionSlot_RejectsBeyondCap_AndReleaseFreesASlot()
    {
        var service = ServiceWithOptions(new ScreenServerService.ServiceOptions { MaxConcurrentConnections = 2 });
        IHubCallback callback = service;

        Assert.True(callback.TryAcquireConnectionSlot("conn-a"));
        Assert.True(callback.TryAcquireConnectionSlot("conn-b"));
        Assert.False(callback.TryAcquireConnectionSlot("conn-c"));

        callback.ReleaseConnectionSlot("conn-a");
        Assert.True(callback.TryAcquireConnectionSlot("conn-c"));
    }

    [Fact]
    public void ReleaseConnectionSlot_ForAConnectionNeverAcquired_IsANoOp()
    {
        IHubCallback callback = _service;
        callback.ReleaseConnectionSlot("conn-never");
        Assert.True(callback.TryAcquireConnectionSlot("conn-a"));
    }

    private bool RegisterOn(ScreenServerService service, string connectionId, string screenId)
    {
        IHubCallback callback = service;
        var nonce = callback.BeginSession(connectionId);
        var payload = RegisterPayload.From(ScreenCapabilities.None).ToJson();
        var mac = ScreenMessageAuth.Sign(KeyFor(screenId), nonce, 1, payload);

        return callback.TryRegisterScreen(connectionId, null, new SignedEnvelope(screenId, 1, payload, mac).ToJson());
    }

    private static ScreenPlaybackState Playing() => new()
    {
        StreamUrl = "http://192.168.1.10:5251/media/abc123/stream.m3u8",
        IsPlaying = true,
        Position = TimeSpan.Zero,
        Duration = TimeSpan.FromMinutes(3),
    };

    /// <summary>Concurrent sends reach the screen in order; a non-advancing sequence is dropped.</summary>
    /// <remarks>Deterministic: the first send is held until the second could overtake.</remarks>
    [Fact]
    public async Task BroadcastCommandAsync_TwoAtOnce_ReachTheScreenInSequenceOrder()
    {
        Register("conn-a", "Screen 1");

        var firstIsHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new List<long>();
        var entered = 0;

        // Recorded after the hold, not before: the question is the order the screen is handed
        // them, which is what its replay guard judges.
        _singleClient.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var seq = SignedEnvelope.TryParse(call.Arg<object?[]>()[0] as string ?? "")!.Seq;

                if (Interlocked.Increment(ref entered) == 1)
                {
                    firstIsHeld.TrySetResult();
                    await release.Task;
                }

                lock (delivered) delivered.Add(seq);
            });

        var first = _service.BroadcastCommandAsync(new PlayCommand());
        await firstIsHeld.Task;

        // Issued while the first is still in the transport. Ordered delivery parks it behind;
        // unordered delivery lets it straight past.
        var second = _service.BroadcastCommandAsync(new PauseCommand());
        var overtook = await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(250))) == second;

        release.TrySetResult();
        await Task.WhenAll(first, second);

        List<long> order;
        lock (delivered) order = [.. delivered];

        Assert.Equal(2, order.Count);
        Assert.False(overtook, "the second command was delivered while the first was still in flight");
        Assert.Equal(order.OrderBy(seq => seq), order);
    }

    /// <summary>Regression test: disconnect must clear the session's key, not just remove the session
    /// entry — a send already queued behind the per-screen gate holds its own reference to that
    /// object, so only nulling the key on it (not just removing it from the dictionary) reaches the
    /// "re-read under the gate" guard in <c>SendToAsync</c>.</summary>
    [Fact]
    public async Task BroadcastCommandAsync_QueuedBehindTheGate_IsDropped_IfTheScreenDisconnectsMeanwhile()
    {
        Register("conn-a", "Screen 1");

        var firstIsHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sendCount = 0;
        var entered = 0;

        _singleClient.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                Interlocked.Increment(ref sendCount);

                if (Interlocked.Increment(ref entered) == 1)
                {
                    firstIsHeld.TrySetResult();
                    await release.Task;
                }
            });

        var first = _service.BroadcastCommandAsync(new PlayCommand());
        await firstIsHeld.Task;

        // Captures its own session reference under the lock (still valid) and then queues behind the
        // send gate the first send is holding.
        var second = _service.BroadcastCommandAsync(new PauseCommand());
        var overtook = await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(250))) == second;
        Assert.False(overtook, "the second send must still be queued behind the gate for this test to prove anything");

        Callback.OnScreenDisconnected("conn-a");

        release.TrySetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, sendCount);
    }

    /// <summary>ScreenConnected must fire after the lock that registered the screen is released, or a
    /// subscriber that calls back into the server (e.g. to broadcast on connect) deadlocks reentering
    /// the non-reentrant lock from inside the very call that still holds it.</summary>
    [Fact]
    public async Task Register_RaisesScreenConnected_AfterReleasingTheLock_SoASubscriberCanCallBackIn()
    {
        _service.ScreenConnected += (_, _) => _service.BroadcastCommandAsync(new PlayCommand()).GetAwaiter().GetResult();

        var registered = Task.Run(() => Register("conn-a", "Screen 1"));
        var finished = await Task.WhenAny(registered, Task.Delay(TimeSpan.FromSeconds(2))) == registered;

        Assert.True(finished, "registering deadlocked: ScreenConnected must be raised after the lock is released");
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
