namespace KHost.Abstractions.Services.IPC;

public interface IScreenServer
{
    event EventHandler<ScreenConnectionEventArgs>? ScreenConnected;
    event EventHandler<ScreenConnectionEventArgs>? ScreenDisconnected;
    event EventHandler<ScreenStateReceivedEventArgs>? StateReceived;

    IAsyncEnumerable<IScreenConnection> GetConnectedScreensAsync();
    Task SendCommandAsync(string screenId, IScreenCommand command);
    Task BroadcastCommandAsync(IScreenCommand command);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}

public interface IScreenConnection
{
    string ScreenId { get; }
    string? ConnectionId { get; }
    DateTime ConnectedAt { get; }
    bool IsConnected { get; }
}

public interface IScreenCommand { }

public interface IScreenState { }

public class ScreenConnectionEventArgs : EventArgs
{
    public required IScreenConnection Connection { get; init; }
}

public class ScreenStateReceivedEventArgs : EventArgs
{
    public required string ScreenId { get; init; }
    public required IScreenState State { get; init; }
}
