using KHost.Abstractions.Services.IPC;
using KHost.IPC.SignalR;
using Microsoft.AspNetCore.SignalR;

namespace KHost.UnitTests.IPC;

public class ScreenServerServiceTests
{
    private readonly IHubContext<ScreenHub> _hubContext = Substitute.For<IHubContext<ScreenHub>>();
    private readonly IHubClients _clients = Substitute.For<IHubClients>();
    private readonly ISingleClientProxy _singleClient = Substitute.For<ISingleClientProxy>();
    private readonly IClientProxy _allClients = Substitute.For<IClientProxy>();
    private readonly ScreenServerService _service;

    public ScreenServerServiceTests()
    {
        _clients.Client(Arg.Any<string>()).Returns(_singleClient);
        _clients.All.Returns(_allClients);
        _hubContext.Clients.Returns(_clients);

        _service = new ScreenServerService(_hubContext);
    }

    private IHubCallback Callback => _service;

    private async Task<List<IScreenConnection>> ConnectedScreensAsync()
    {
        var list = new List<IScreenConnection>();
        await foreach (var conn in _service.GetConnectedScreensAsync())
            list.Add(conn);
        return list;
    }

    [Fact]
    public async Task GetConnectedScreensAsync_IsEmpty_Initially()
    {
        Assert.Empty(await ConnectedScreensAsync());
    }

    [Fact]
    public async Task OnScreenConnected_TracksTheConnection()
    {
        Callback.OnScreenConnected("Screen 1", "conn-a", ScreenCapabilities.None);

        var screens = await ConnectedScreensAsync();

        var only = Assert.Single(screens);
        Assert.Equal("Screen 1", only.ScreenId);
        Assert.Equal("conn-a", only.ConnectionId);
        Assert.True(only.IsConnected);
    }

    [Fact]
    public void OnScreenConnected_RaisesScreenConnected()
    {
        ScreenConnectionEventArgs? captured = null;
        _service.ScreenConnected += (_, e) => captured = e;

        Callback.OnScreenConnected("Screen 1", "conn-a", ScreenCapabilities.None);

        Assert.NotNull(captured);
        Assert.Equal("Screen 1", captured.Connection.ScreenId);
    }

    [Fact]
    public async Task OnScreenConnected_WithDuplicateScreenId_CollapsesToOneConnection()
    {
        Callback.OnScreenConnected("Screen", "conn-a", ScreenCapabilities.None);
        Callback.OnScreenConnected("Screen", "conn-b", ScreenCapabilities.None);

        var screens = await ConnectedScreensAsync();

        // Connections are keyed by screen id, so two screens sharing an id cannot both be tracked.
        // This is what the unquoted --screen-id argument bug produced at runtime.
        var only = Assert.Single(screens);
        Assert.Equal("conn-b", only.ConnectionId);
    }

    [Fact]
    public async Task OnScreenDisconnected_RemovesTheMatchingConnection()
    {
        Callback.OnScreenConnected("Screen 1", "conn-a", ScreenCapabilities.None);
        Callback.OnScreenConnected("Screen 2", "conn-b", ScreenCapabilities.None);

        Callback.OnScreenDisconnected("conn-a");

        var screens = await ConnectedScreensAsync();

        var only = Assert.Single(screens);
        Assert.Equal("Screen 2", only.ScreenId);
    }

    [Fact]
    public void OnScreenDisconnected_RaisesScreenDisconnected()
    {
        Callback.OnScreenConnected("Screen 1", "conn-a", ScreenCapabilities.None);

        ScreenConnectionEventArgs? captured = null;
        _service.ScreenDisconnected += (_, e) => captured = e;

        Callback.OnScreenDisconnected("conn-a");

        Assert.NotNull(captured);
        Assert.Equal("Screen 1", captured.Connection.ScreenId);
    }

    [Fact]
    public async Task OnScreenDisconnected_IsANoOp_ForUnknownConnectionId()
    {
        Callback.OnScreenConnected("Screen 1", "conn-a", ScreenCapabilities.None);

        var raised = false;
        _service.ScreenDisconnected += (_, _) => raised = true;

        Callback.OnScreenDisconnected("conn-unknown");

        Assert.False(raised);
        Assert.Single(await ConnectedScreensAsync());
    }

    [Fact]
    public async Task OnScreenDisconnected_ForStaleConnectionId_LeavesReconnectedScreenTracked()
    {
        Callback.OnScreenConnected("Screen 1", "conn-old", ScreenCapabilities.None);
        Callback.OnScreenConnected("Screen 1", "conn-new", ScreenCapabilities.None);

        // A late disconnect for the superseded connection must not evict the live one.
        Callback.OnScreenDisconnected("conn-old");

        var only = Assert.Single(await ConnectedScreensAsync());
        Assert.Equal("conn-new", only.ConnectionId);
    }

    [Fact]
    public void OnStateReceived_RaisesStateReceived()
    {
        ScreenStateReceivedEventArgs? captured = null;
        _service.StateReceived += (_, e) => captured = e;

        var state = new ScreenPlaybackState
        {
            LoadedFilePath = "/music/x.mp4",
            IsPlaying = true,
            Position = TimeSpan.Zero,
            Duration = TimeSpan.FromMinutes(3),
        };

        Callback.OnStateReceived("Screen 1", state);

        Assert.NotNull(captured);
        Assert.Equal("Screen 1", captured.ScreenId);
        Assert.Same(state, captured.State);
    }

    [Fact]
    public async Task SendCommandAsync_SendsSerializedCommandToTheMatchingConnection()
    {
        Callback.OnScreenConnected("Screen 1", "conn-a", ScreenCapabilities.None);

        await _service.SendCommandAsync("Screen 1", new PlayCommand());

        _clients.Received(1).Client("conn-a");
        await _singleClient.Received(1).SendCoreAsync(
            "ReceiveCommand",
            Arg.Is<object?[]>(args => args.Length == 1 && ((string)args[0]!).Contains("$type")),
            Arg.Any<CancellationToken>());
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
        Callback.OnScreenConnected("Screen 1", "conn-a", ScreenCapabilities.None);
        Callback.OnScreenDisconnected("conn-a");

        await _service.SendCommandAsync("Screen 1", new PlayCommand());

        await _singleClient.DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendCommandAsync_SerializesThroughTheBaseType()
    {
        Callback.OnScreenConnected("Screen 1", "conn-a", ScreenCapabilities.None);

        string? payload = null;
        await _singleClient.SendCoreAsync(
            Arg.Any<string>(),
            Arg.Do<object?[]>(args => payload = args[0] as string),
            Arg.Any<CancellationToken>());

        await _service.SendCommandAsync("Screen 1", new SeekCommand { Position = TimeSpan.FromSeconds(30) });

        // Serializing by runtime type would drop the $type discriminator and the screen's
        // base-typed deserialize would throw.
        Assert.NotNull(payload);
        var back = System.Text.Json.JsonSerializer.Deserialize<ScreenCommandBase>(
            payload, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.IsType<SeekCommand>(back).Position);
    }

}
