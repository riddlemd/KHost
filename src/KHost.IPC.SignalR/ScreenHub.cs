using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace KHost.IPC.SignalR;

internal sealed class ScreenHub : Hub
{
    private readonly IHubCallback _callback;
    private readonly ILogger<ScreenHub> _logger;

    public ScreenHub(IHubCallback callback, ILogger<ScreenHub> logger)
    {
        _callback = callback;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        // Rejected before the cap-holder set gains an entry: Abort() still runs OnDisconnectedAsync,
        // whose ReleaseConnectionSlot is a no-op for a connectionId that was never acquired.
        if (!_callback.TryAcquireConnectionSlot(Context.ConnectionId))
        {
            _logger.LogWarning("Dropped connection {ConnectionId}: the connection cap is full", Context.ConnectionId);
            Context.Abort();
            return;
        }

        // The nonce every message on this connection must be signed against. Sent before the screen
        // registers, so the screen can prove it holds the key without the key ever crossing the wire.
        var nonce = _callback.BeginSession(Context.ConnectionId);
        await Clients.Caller.SendAsync("Session", nonce);

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _callback.ReleaseConnectionSlot(Context.ConnectionId);
        _callback.OnScreenDisconnected(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public Task RegisterScreenAsync(string envelopeJson)
    {
        // The address this screen connected to, not one we pick: a host with several interfaces
        // must hand each screen the one it already routed to.
        var hostAddress = Context.GetHttpContext()?.Connection.LocalIpAddress?.ToString();

        // A registration that does not verify is a stranger or a forgery. Drop the connection
        // rather than leave it half-open.
        if (!_callback.TryRegisterScreen(Context.ConnectionId, hostAddress, envelopeJson))
        {
            _logger.LogWarning("Dropped connection {ConnectionId}: its registration was refused", Context.ConnectionId);
            Context.Abort();
        }

        return Task.CompletedTask;
    }

    /// <summary>Does nothing else: any work lands inside the round trip being measured.</summary>
    public long EchoClock()
    {
        // Only a registered screen has any use for the clock, and gating it keeps an unauthenticated
        // peer from using the hub as a timing oracle before it has proven anything.
        if (!_callback.IsAuthenticated(Context.ConnectionId))
        {
            _logger.LogWarning("Dropped connection {ConnectionId}: it asked for the clock before registering", Context.ConnectionId);
            Context.Abort();
            return 0;
        }

        return DateTime.UtcNow.Ticks;
    }

    public Task ReceiveStateAsync(string envelopeJson)
    {
        // A state report drives the host's playback clock, so a forged one is exactly the spoof this
        // guards against: verify or drop the connection.
        if (!_callback.TryAcceptState(Context.ConnectionId, envelopeJson))
        {
            _logger.LogWarning("Dropped connection {ConnectionId}: its state report was refused", Context.ConnectionId);
            Context.Abort();
        }

        return Task.CompletedTask;
    }
}
