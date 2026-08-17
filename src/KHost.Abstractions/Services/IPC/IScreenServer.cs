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
    ScreenCapabilities Capabilities { get; }
}

/// <summary>
/// What a screen can do, declared when it registers. Screens differ enough that the host cannot
/// infer this: a Cast device takes a URL and plays it on its own schedule, with no way to be held
/// to someone else's.
/// </summary>
public sealed class ScreenCapabilities
{
    /// <summary>Conservative default — an unknown screen neither syncs nor is trusted with audio.</summary>
    public static readonly ScreenCapabilities None = new();

    /// <summary>
    /// What a Cast receiver reports: it renders both tracks for the room, but plays on its own
    /// schedule with no way to be held to anyone else's, so it can never join the synced group.
    /// </summary>
    public static readonly ScreenCapabilities CastDevice = new()
    {
        SupportsAudio = true,
        SupportsVideo = true,
    };

    /// <summary>
    /// True when the screen can follow a scheduled start and correct itself onto the group's
    /// timeline. Only such screens can be kept frame-close to each other; everything else is a
    /// loose consumer that plays the same media whenever it manages to.
    /// </summary>
    public bool SupportsSync { get; init; }

    /// <summary>
    /// True when the screen renders audio. The audio role is drawn from these, and deliberately
    /// does not require <see cref="SupportsSync"/> — a Cast device can carry the room's sound
    /// without ever being holdable to a schedule.
    /// </summary>
    public bool SupportsAudio { get; init; }

    /// <summary>
    /// True when the screen renders video. Independent of <see cref="SupportsAudio"/>: the screen
    /// the room hears may be an audio-only output, leaving the lyrics displays as the only things
    /// showing video while still taking their timing from elsewhere.
    /// </summary>
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
