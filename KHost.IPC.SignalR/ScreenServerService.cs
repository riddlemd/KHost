using KHost.Abstractions.Services.IPC;
using Microsoft.AspNetCore.SignalR;

namespace KHost.IPC.SignalR;

internal sealed class ScreenServerService : IScreenServer, IHubCallback
{
    private readonly IHubContext<ScreenHub> _hubContext;
    private readonly Dictionary<string, ScreenConnection> _connections = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public event EventHandler<ScreenConnectionEventArgs>? ScreenConnected;
    public event EventHandler<ScreenConnectionEventArgs>? ScreenDisconnected;
    public event EventHandler<ScreenStateReceivedEventArgs>? StateReceived;

    public ScreenServerService(IHubContext<ScreenHub> hubContext) => _hubContext = hubContext;

    void IHubCallback.OnScreenConnected(string screenId, string connectionId)
    {
        _lock.Wait();
        try
        {
            var conn = new ScreenConnection
            {
                ScreenId = screenId,
                ConnectionId = connectionId,
                ConnectedAt = DateTime.UtcNow
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

    public async Task SendCommandAsync(string screenId, IScreenCommand command)
    {
        string? connectionId;
        await _lock.WaitAsync();
        try { _connections.TryGetValue(screenId, out var conn); connectionId = conn?.ConnectionId; }
        finally { _lock.Release(); }

        if (connectionId is not null)
            await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveCommand", ScreenIpcSerializer.SerializeCommand(command));
    }

    public Task BroadcastCommandAsync(IScreenCommand command) =>
        _hubContext.Clients.All.SendAsync("ReceiveCommand", ScreenIpcSerializer.SerializeCommand(command));

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;

    private sealed class ScreenConnection : IScreenConnection
    {
        public required string ScreenId { get; init; }
        public required string ConnectionId { get; init; }
        public DateTime ConnectedAt { get; init; }
        public bool IsConnected => true;
    }
}
