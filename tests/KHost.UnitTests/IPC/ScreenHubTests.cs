using KHost.IPC.SignalR;
using Microsoft.AspNetCore.SignalR;

namespace KHost.UnitTests.IPC;

public class ScreenHubTests
{
    private readonly IHubCallback _callback = Substitute.For<IHubCallback>();
    private readonly HubCallerContext _context = Substitute.For<HubCallerContext>();
    private readonly IHubCallerClients _clients = Substitute.For<IHubCallerClients>();
    private readonly ISingleClientProxy _caller = Substitute.For<ISingleClientProxy>();
    private readonly ScreenHub _hub;

    public ScreenHubTests()
    {
        _context.ConnectionId.Returns("conn-a");
        _clients.Caller.Returns(_caller);
        _callback.BeginSession("conn-a").Returns("nonce-xyz");
        _hub = new ScreenHub(_callback) { Context = _context, Clients = _clients };
    }

    [Fact]
    public async Task OnConnectedAsync_WithASlotAvailable_SendsTheSessionNonce()
    {
        _callback.TryAcquireConnectionSlot("conn-a").Returns(true);

        await _hub.OnConnectedAsync();

        _context.DidNotReceive().Abort();
        // The nonce the screen must sign every message with, sent before it registers.
        await _caller.Received(1).SendCoreAsync(
            "Session",
            Arg.Is<object?[]>(a => a.Length == 1 && (string)a[0]! == "nonce-xyz"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnectedAsync_BeyondTheConnectionCap_AbortsBeforeBeginningASession()
    {
        _callback.TryAcquireConnectionSlot("conn-a").Returns(false);

        await _hub.OnConnectedAsync();

        _context.Received(1).Abort();
        _callback.DidNotReceive().BeginSession(Arg.Any<string>());
    }

    [Fact]
    public void RegisterScreenAsync_ThatDoesNotVerify_AbortsTheConnection()
    {
        _callback.TryRegisterScreen("conn-a", Arg.Any<string?>(), "bad-envelope").Returns(false);

        _hub.RegisterScreenAsync("bad-envelope");

        _context.Received(1).Abort();
    }

    [Fact]
    public void ReceiveStateAsync_ThatDoesNotVerify_AbortsTheConnection()
    {
        _callback.TryAcceptState("conn-a", "bad-envelope").Returns(false);

        _hub.ReceiveStateAsync("bad-envelope");

        _context.Received(1).Abort();
    }

    [Fact]
    public void EchoClock_FromAnUnauthenticatedConnection_Aborts()
    {
        _callback.IsAuthenticated("conn-a").Returns(false);

        var ticks = _hub.EchoClock();

        Assert.Equal(0, ticks);
        _context.Received(1).Abort();
    }

    [Fact]
    public void EchoClock_FromAnAuthenticatedConnection_ReturnsTheClock()
    {
        _callback.IsAuthenticated("conn-a").Returns(true);

        Assert.True(_hub.EchoClock() > 0);
        _context.DidNotReceive().Abort();
    }

    [Fact]
    public void OnDisconnectedAsync_ReleasesTheConnectionSlot()
    {
        _hub.OnDisconnectedAsync(exception: null);

        _callback.Received(1).ReleaseConnectionSlot("conn-a");
    }

    [Fact]
    public void OnDisconnectedAsync_NotifiesScreenDisconnection()
    {
        _hub.OnDisconnectedAsync(exception: null);

        _callback.Received(1).OnScreenDisconnected("conn-a");
    }
}
