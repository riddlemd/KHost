using System.Security.Cryptography;
using KHost.Abstractions.Services.IPC;
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

    // --- signed-handshake helpers ------------------------------------------------

    private byte[] KeyFor(string screenId)
    {
        if (_keys.GetKey(screenId) is not { } key)
        {
            key = RandomNumberGenerator.GetBytes(32);
            _keys.Set(screenId, key);
        }

        return key;
    }

    private string Nonce(string connectionId)
    {
        if (!_nonces.TryGetValue(connectionId, out var nonce))
            _nonces[connectionId] = nonce = Callback.BeginSession(connectionId);

        return nonce;
    }

    private bool Register(
        string connectionId, string screenId, string? hostAddress = null,
        ScreenCapabilities? capabilities = null, long seq = 1, byte[]? signWith = null, string? nonceOverride = null)
    {
        var nonce = nonceOverride ?? Nonce(connectionId);
        var payload = RegisterPayload.From(capabilities ?? ScreenCapabilities.None).ToJson();
        var mac = ScreenMessageAuth.Sign(signWith ?? KeyFor(screenId), nonce, seq, payload);

        return Callback.TryRegisterScreen(connectionId, hostAddress, new SignedEnvelope(screenId, seq, payload, mac).ToJson());
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

    // --- registration & tracking -------------------------------------------------

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
        Register("conn-a", "Screen 1", capabilities: new ScreenCapabilities { SupportsSync = true, SupportsAudio = true });

        var only = Assert.Single(await ConnectedScreensAsync());
        Assert.True(only.Capabilities.SupportsSync);
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

    // --- authentication rejections ----------------------------------------------

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

    // --- state, signed and replay-guarded ---------------------------------------

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

    // --- disconnect --------------------------------------------------------------

    [Fact]
    public async Task OnScreenDisconnected_RemovesTheMatchingConnection()
    {
        Register("conn-a", "Screen 1");
        Register("conn-b", "Screen 2");

        Callback.OnScreenDisconnected("conn-a");

        var only = Assert.Single(await ConnectedScreensAsync());
        Assert.Equal("Screen 2", only.ScreenId);
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

    // --- sending commands, signed -----------------------------------------------

    [Fact]
    public async Task SendCommandAsync_SendsASignedCommandToTheMatchingConnection()
    {
        Register("conn-a", "Screen 1");

        string? sent = null;
        await _singleClient.SendCoreAsync(
            Arg.Any<string>(), Arg.Do<object?[]>(a => sent = a[0] as string), Arg.Any<CancellationToken>());

        await _service.SendCommandAsync("Screen 1", new PlayCommand());

        _clients.Received().Client("conn-a");

        var envelope = SignedEnvelope.TryParse(sent!);
        Assert.NotNull(envelope);
        Assert.Equal("Screen 1", envelope!.ScreenId);
        // The command is inside, and the MAC verifies against the screen's key and session nonce.
        Assert.True(ScreenMessageAuth.Verify(KeyFor("Screen 1"), Nonce("conn-a"), envelope.Seq, envelope.Payload, envelope.Mac));
        Assert.Contains("$type", envelope.Payload);
    }

    [Fact]
    public async Task SendCommandAsync_IsANoOp_ForUnknownScreenId()
    {
        await _service.SendCommandAsync("Nope", new PlayCommand());

        await _singleClient.DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendCommandAsync_DoesNotSend_AfterTheScreenDisconnects()
    {
        Register("conn-a", "Screen 1");
        Callback.OnScreenDisconnected("conn-a");

        await _service.SendCommandAsync("Screen 1", new PlayCommand());

        await _singleClient.DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendCommandAsync_RewritesTheStreamUrlForThatScreen()
    {
        Register("conn-across", "Across", hostAddress: "192.168.0.99");

        string? sent = null;
        await _singleClient.SendCoreAsync(
            Arg.Any<string>(), Arg.Do<object?[]>(a => sent = a[0] as string), Arg.Any<CancellationToken>());

        await _service.SendCommandAsync("Across", new LoadMediaCommand
        {
            StreamUrl = "http://localhost:5251/media/abc/stream.m3u8",
        });

        Assert.Contains("192.168.0.99:5251", InnerPayload(sent));
    }

    [Fact]
    public async Task BroadcastCommandAsync_GivesEachScreenAStreamUrlItCanReach()
    {
        var here = Substitute.For<ISingleClientProxy>();
        var across = Substitute.For<ISingleClientProxy>();
        _clients.Client("conn-here").Returns(here);
        _clients.Client("conn-across").Returns(across);

        Register("conn-here", "Here", hostAddress: "127.0.0.1");
        Register("conn-across", "Across", hostAddress: "192.168.0.99");

        string? herePayload = null, acrossPayload = null;
        await here.SendCoreAsync(Arg.Any<string>(), Arg.Do<object?[]>(a => herePayload = a[0] as string), Arg.Any<CancellationToken>());
        await across.SendCoreAsync(Arg.Any<string>(), Arg.Do<object?[]>(a => acrossPayload = a[0] as string), Arg.Any<CancellationToken>());

        await _service.BroadcastCommandAsync(new LoadMediaCommand
        {
            StreamUrl = "http://localhost:5251/media/abc/stream.m3u8",
        });

        Assert.Contains("localhost:5251", InnerPayload(herePayload));
        Assert.Contains("192.168.0.99:5251", InnerPayload(acrossPayload));
        Assert.DoesNotContain("localhost:5251", InnerPayload(acrossPayload));
    }

    [Fact]
    public async Task BroadcastCommandAsync_FansOutPerScreen_EvenWithNoUrlToRewrite()
    {
        // No Clients.All any more: each screen's command carries its own MAC, so a broadcast is a
        // per-connection send even for a command with nothing screen-specific in it.
        Register("conn-a", "Screen 1");

        await _service.BroadcastCommandAsync(new PlayCommand());

        await _singleClient.Received(1).SendCoreAsync(
            "ReceiveCommand", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendCommandAsync_SerializesTheCommandThroughItsBaseType()
    {
        Register("conn-a", "Screen 1");

        string? sent = null;
        await _singleClient.SendCoreAsync(
            Arg.Any<string>(), Arg.Do<object?[]>(a => sent = a[0] as string), Arg.Any<CancellationToken>());

        await _service.SendCommandAsync("Screen 1", new SeekCommand { Position = TimeSpan.FromSeconds(30) });

        var back = System.Text.Json.JsonSerializer.Deserialize<ScreenCommandBase>(
            InnerPayload(sent)!, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.IsType<SeekCommand>(back).Position);
    }

    // --- caps (carried over from the throttle work, now over the signed path) ----

    [Fact]
    public async Task Register_BeyondScreenCap_IsRefused_AndDoesNotRaiseScreenConnected()
    {
        var service = ServiceWithOptions(new ScreenServerService.ServiceOptions { MaxRegisteredScreens = 2 });
        var raised = 0;
        service.ScreenConnected += (_, _) => raised++;

        RegisterOn(service, "conn-a", "Screen 1");
        RegisterOn(service, "conn-b", "Screen 2");
        Assert.False(RegisterOn(service, "conn-c", "Screen 3"));

        var screens = await ConnectedScreensAsync(service);
        Assert.Equal(2, screens.Count);
        Assert.DoesNotContain(screens, s => s.ScreenId == "Screen 3");
        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task Register_ReRegistrationOfAnExistingIdAtCap_StillOverwrites()
    {
        var service = ServiceWithOptions(new ScreenServerService.ServiceOptions { MaxRegisteredScreens = 1 });

        RegisterOn(service, "conn-a", "Screen 1");
        RegisterOn(service, "conn-b", "Screen 1");

        var only = Assert.Single(await ConnectedScreensAsync(service));
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

    // Registers on an alternate service instance (used by the cap tests), with its own session/key.
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
