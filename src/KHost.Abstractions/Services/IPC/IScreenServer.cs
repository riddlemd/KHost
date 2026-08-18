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

/// <summary>Declared at registration — the host cannot infer it.</summary>
public sealed class ScreenCapabilities
{
    /// <summary>Conservative default for an unknown screen.</summary>
    public static readonly ScreenCapabilities None = new();

    /// <summary>What a Cast receiver reports: both tracks, but never syncable.</summary>
    public static readonly ScreenCapabilities CastDevice = new()
    {
        SupportsAudio = true,
        SupportsVideo = true,
    };

    /// <summary>Only these can be kept frame-close; everything else is a loose consumer.</summary>
    public bool SupportsSync { get; init; }

    /// <summary>The audio role is drawn from these, and does not require sync.</summary>
    public bool SupportsAudio { get; init; }

    /// <summary>Independent of audio: the screen the room hears may be audio-only.</summary>
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
