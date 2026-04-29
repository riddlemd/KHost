namespace KHost.Abstractions.Services.IPC;

public interface IScreenClient
{
    event EventHandler<ScreenCommandReceivedEventArgs>? CommandReceived;
    event EventHandler<ScreenClientStateChangedEventArgs>? StateChanged;

    string? ScreenId { get; }
    ScreenClientState State { get; }

    Task ConnectAsync(string serverUri, string screenId, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task SendStateAsync(IScreenState state);
}

public class ScreenCommandReceivedEventArgs : EventArgs
{
    public required IScreenCommand Command { get; init; }
}

public class ScreenClientStateChangedEventArgs : EventArgs
{
    public required ScreenClientState OldState { get; init; }
    public required ScreenClientState NewState { get; init; }
}

public enum ScreenClientState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Error
}
