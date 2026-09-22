namespace KHost.Abstractions.Services.IPC;

public interface IScreenServer
{
    event EventHandler<ScreenConnectionEventArgs>? ScreenConnected;
    event EventHandler<ScreenConnectionEventArgs>? ScreenDisconnected;
    event EventHandler<ScreenStateReceivedEventArgs>? StateReceived;

    IAsyncEnumerable<IScreenConnection> GetConnectedScreensAsync();
    Task SendCommandAsync(string screenId, IScreenCommand command);
    Task BroadcastCommandAsync(IScreenCommand command);
}

public interface IScreenConnection
{
    string ScreenId { get; }
    string? ConnectionId { get; }
    DateTime ConnectedAt { get; }
    bool IsConnected { get; }
    ScreenCapabilities Capabilities { get; }
}

/// <summary>Declared at registration; the host cannot infer it.</summary>
public sealed class ScreenCapabilities
{
    /// <summary>Conservative default for an unknown screen.</summary>
    public static readonly ScreenCapabilities None = new();

    public bool SupportsAudio { get; init; }

    /// <summary>Independent of audio: a screen may carry the room with no picture at all.</summary>
    public bool SupportsVideo { get; init; }
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
