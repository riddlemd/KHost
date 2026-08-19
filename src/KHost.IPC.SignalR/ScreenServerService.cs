using KHost.Abstractions.Services.IPC;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace KHost.IPC.SignalR;

internal sealed class ScreenServerService : IScreenServer, IHubCallback
{
    private readonly IHubContext<ScreenHub> _hubContext;
    private readonly ILogger<ScreenServerService>? _logger;
    private readonly Dictionary<string, ScreenConnection> _connections = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public event EventHandler<ScreenConnectionEventArgs>? ScreenConnected;
    public event EventHandler<ScreenConnectionEventArgs>? ScreenDisconnected;
    public event EventHandler<ScreenStateReceivedEventArgs>? StateReceived;

    public ScreenServerService(IHubContext<ScreenHub> hubContext, ILogger<ScreenServerService>? logger = null)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    void IHubCallback.OnScreenConnected(string screenId, string connectionId, string? hostAddress, ScreenCapabilities capabilities)
    {
        _lock.Wait();
        try
        {
            var conn = new ScreenConnection
            {
                ScreenId = screenId,
                ConnectionId = connectionId,
                ConnectedAt = DateTime.UtcNow,
                HostAddress = hostAddress,
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

    public async Task SendCommandAsync(string screenId, IScreenCommand command)
    {
        ScreenConnection? conn;
        await _lock.WaitAsync();
        try { _connections.TryGetValue(screenId, out conn); }
        finally { _lock.Release(); }

        if (conn is null) return;

        await SendToAsync(conn, command);
    }

    public async Task BroadcastCommandAsync(IScreenCommand command)
    {
        // A stream URL differs per screen, so those fan out instead of going to Clients.All. The
        // cost is that a client which has not registered yet is skipped — it has no screen id, so
        // the host could not have addressed it anyway.
        if (command is not LoadMediaCommand { StreamUrl.Length: > 0 })
        {
            await _hubContext.Clients.All.SendAsync("ReceiveCommand", ScreenIpcSerializer.SerializeCommand(command));
            return;
        }

        List<ScreenConnection> snapshot;
        await _lock.WaitAsync();
        try { snapshot = [.. _connections.Values]; }
        finally { _lock.Release(); }

        await Task.WhenAll(snapshot.Select(conn => SendToAsync(conn, command)));
    }

    private Task SendToAsync(ScreenConnection connection, IScreenCommand command)
        => _hubContext.Clients.Client(connection.ConnectionId)
            .SendAsync("ReceiveCommand", ScreenIpcSerializer.SerializeCommand(Reachable(command, connection)));

    private IScreenCommand Reachable(IScreenCommand command, ScreenConnection connection)
    {
        if (command is not LoadMediaCommand load) return command;

        var url = StreamUrlRewriter.ForScreen(load.StreamUrl, connection.HostAddress);
        if (ReferenceEquals(url, load.StreamUrl)) return command;

        // Worth a line: when a screen plays nothing, the first question is which address it was
        // told to fetch from.
        _logger?.LogInformation("Screen {ScreenId} will fetch the stream from {Url}", connection.ScreenId, url);

        return new LoadMediaCommand
        {
            StreamUrl = url,
            StreamStartOffset = load.StreamStartOffset,
        };
    }

    private sealed class ScreenConnection : IScreenConnection
    {
        public required string ScreenId { get; init; }
        public required string ConnectionId { get; init; }
        public DateTime ConnectedAt { get; init; }
        public string? HostAddress { get; init; }
        public bool IsConnected => true;
        public ScreenCapabilities Capabilities { get; init; } = ScreenCapabilities.None;
    }
}
