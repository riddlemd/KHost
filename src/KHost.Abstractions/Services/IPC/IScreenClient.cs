namespace KHost.Abstractions.Services.IPC;

public interface IScreenClient
{
    event EventHandler<ScreenCommandReceivedEventArgs>? CommandReceived;
    event EventHandler<ScreenClientStateChangedEventArgs>? StateChanged;

    string? ScreenId { get; }
    ScreenClientState State { get; }

    Task ConnectAsync(
        string serverUri,
        string screenId,
        ScreenCapabilities? capabilities = null,
        byte[]? authKey = null,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync();

    /// <summary>Skipped, not thrown, while the screen is not registered on a live session.</summary>
    Task SendStateAsync(IScreenState state);

    /// <summary>Estimates the offset to the host's clock, NTP style, for a scheduled start.</summary>
    Task<TimeSpan> EstimateClockOffsetAsync(CancellationToken cancellationToken = default);
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
