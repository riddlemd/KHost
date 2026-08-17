using KHost.Abstractions.Services.IPC;
using Microsoft.AspNetCore.SignalR;

namespace KHost.IPC.SignalR;

internal sealed class ScreenServerService : IScreenTransport, IHubCallback
{
    private readonly IHubContext<ScreenHub> _hubContext;
    private readonly Dictionary<string, ScreenConnection> _connections = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public event EventHandler<ScreenConnectionEventArgs>? ScreenConnected;
    public event EventHandler<ScreenConnectionEventArgs>? ScreenDisconnected;
    public event EventHandler<ScreenStateReceivedEventArgs>? StateReceived;

    public ScreenServerService(IHubContext<ScreenHub> hubContext) => _hubContext = hubContext;

    void IHubCallback.OnScreenConnected(string screenId, string connectionId, ScreenCapabilities capabilities)
    {
        _lock.Wait();
        try
        {
            var conn = new ScreenConnection
            {
                ScreenId = screenId,
                ConnectionId = connectionId,
                ConnectedAt = DateTime.UtcNow,
                Capabilities = capabilities,
            };
            _connections[screenId] = conn;
            ScreenConnected?.Invoke(this, new ScreenConnectionEventArgs { Connection = conn });
        }
        finally { _lock.Release(); }
    }

    void IHubCallback.OnScreenDisconnected(string connectionId)
    {
        _lock.Wait();
        try
        {
            var conn = _connections.Values.FirstOrDefault(c => c.ConnectionId == connectionId);
            if (conn is null) return;
            _connections.Remove(conn.ScreenId);
            ScreenDisconnected?.Invoke(this, new ScreenConnectionEventArgs { Connection = conn });
        }
        finally { _lock.Release(); }
    }

    void IHubCallback.OnStateReceived(string screenId, IScreenState state) =>
        StateReceived?.Invoke(this, new ScreenStateReceivedEventArgs { ScreenId = screenId, State = state });

    public async IAsyncEnumerable<IScreenConnection> GetConnectedScreensAsync()
    {
        List<IScreenConnection> snapshot;
        await _lock.WaitAsync();
        try { snapshot = [.. _connections.Values]; }
        finally { _lock.Release(); }

        foreach (var conn in snapshot)
            yield return conn;
    }

    public async Task<bool> SendCommandAsync(string screenId, IScreenCommand command)
    {
        string? connectionId;
        await _lock.WaitAsync();
        try { _connections.TryGetValue(screenId, out var conn); connectionId = conn?.ConnectionId; }
        finally { _lock.Release(); }

        // Not ours: the screen is on another transport, and the server will try that one next.
        if (connectionId is null) return false;

        await _hubContext.Clients.Client(connectionId)
            .SendAsync("ReceiveCommand", ScreenIpcSerializer.SerializeCommand(command));

        return true;
    }

    private sealed class ScreenConnection : IScreenConnection
    {
        public required string ScreenId { get; init; }
        public required string ConnectionId { get; init; }
        public DateTime ConnectedAt { get; init; }
        public bool IsConnected => true;
        public ScreenCapabilities Capabilities { get; init; } = ScreenCapabilities.None;
    }
}
