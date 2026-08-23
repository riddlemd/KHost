using KHost.IPC.SignalR;
using Microsoft.AspNetCore.SignalR;

namespace KHost.UnitTests.IPC;

public class ScreenHubTests
{
    private readonly IHubCallback _callback = Substitute.For<IHubCallback>();
    private readonly HubCallerContext _context = Substitute.For<HubCallerContext>();
    private readonly ScreenHub _hub;

    public ScreenHubTests()
    {
        _context.ConnectionId.Returns("conn-a");
        _hub = new ScreenHub(_callback) { Context = _context };
    }

    [Fact]
    public async Task OnConnectedAsync_WithASlotAvailable_AcceptsTheConnection()
    {
        _callback.TryAcquireConnectionSlot("conn-a").Returns(true);

        await _hub.OnConnectedAsync();

        _context.DidNotReceive().Abort();
    }

    [Fact]
    public async Task OnConnectedAsync_BeyondTheConnectionCap_AbortsTheConnection()
    {
        _callback.TryAcquireConnectionSlot("conn-a").Returns(false);

        await _hub.OnConnectedAsync();

        _context.Received(1).Abort();
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
