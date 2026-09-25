namespace KHost.IPC.SignalR.Contracts;

/// <summary>The host's end of the link to its own LocalScreen app: who is connected, what they
/// report, and the commands sent to them.</summary>
/// <remarks>
/// <para>A singleton. Only a screen that proves it holds a key from <see cref="IScreenKeyStore"/>
/// counts as connected, and at most one screen is registered at a time: a second screen id is
/// refused until the first leaves, while the same id reconnecting replaces itself.</para>
/// <para>The events are raised synchronously on the connection's own thread. A handler that does
/// more than record the fact should hand the work off rather than block or wait on a lock.</para>
/// </remarks>
public interface IScreenServer
{
    /// <summary>A screen authenticated and registered; raised again when it re-registers after a
    /// dropped link.</summary>
    event EventHandler<ScreenConnectionEventArgs>? ScreenConnected;

    /// <summary>A registered screen's link closed. Not raised for one that never registered.</summary>
    event EventHandler<ScreenConnectionEventArgs>? ScreenDisconnected;

    /// <summary>A registered screen reported its state (<see cref="ScreenPlaybackState"/> or
    /// <see cref="ScreenBackgroundState"/>); reports that fail authentication never arrive here.</summary>
    event EventHandler<ScreenStateReceivedEventArgs>? StateReceived;

    /// <summary>A snapshot of the registered screens; empty when none is.</summary>
    IAsyncEnumerable<IScreenConnection> GetConnectedScreensAsync();

    /// <summary>Sends <paramref name="command"/> to every registered screen, completing once each
    /// send has finished.</summary>
    /// <remarks>A screen still mid-handshake is skipped, not queued. A <see cref="LoadMediaCommand"/>
    /// has its stream address adjusted per screen to one that screen can reach.</remarks>
    /// <param name="command">One of the <see cref="ScreenCommandBase"/> types; nothing else can be
    /// sent.</param>
    Task BroadcastCommandAsync(IScreenCommand command);
}

/// <summary>One registered screen, as <see cref="IScreenServer"/> sees it.</summary>
/// <remarks>A snapshot: it does not change after it is
/// handed out.</remarks>
public interface IScreenConnection
{
    /// <summary>The name the screen registered under; stable across its reconnects.</summary>
    string ScreenId { get; }

    /// <summary>The transport's id for the current link; changes when the screen reconnects.</summary>
    string? ConnectionId { get; }

    /// <summary>When the screen registered, in UTC.</summary>
    DateTime ConnectedAt { get; }

    /// <summary>Whether the link was up when this snapshot was taken.</summary>
    bool IsConnected { get; }

    /// <summary>What the screen declared it can play when it registered.</summary>
    ScreenCapabilities Capabilities { get; }
}

/// <summary>Declared at registration; the host cannot infer it.</summary>
public sealed class ScreenCapabilities
{
    /// <summary>Conservative default for an unknown screen.</summary>
    public static readonly ScreenCapabilities None = new();

    /// <summary>Plays the song's sound.</summary>
    public bool SupportsAudio { get; init; }

    /// <summary>Independent of audio: a screen may carry the room with no picture at all.</summary>
    public bool SupportsVideo { get; init; }
}

/// <summary>Marks a message the host sends to its LocalScreen app.</summary>
/// <remarks>Only the <see cref="ScreenCommandBase"/>
/// types travel; implementing this marker on another type makes nothing sendable.</remarks>
public interface IScreenCommand { }

/// <summary>Marks a report the LocalScreen app sends back to the host.</summary>
/// <remarks>Only the <see cref="ScreenStateBase"/> types travel.</remarks>
public interface IScreenState { }

/// <summary>Carries the screen for <see cref="IScreenServer.ScreenConnected"/> and
/// <see cref="IScreenServer.ScreenDisconnected"/>.</summary>
public class ScreenConnectionEventArgs : EventArgs
{
    /// <summary>The screen that registered or went away.</summary>
    public required IScreenConnection Connection { get; init; }
}

/// <summary>Carries one report for <see cref="IScreenServer.StateReceived"/>.</summary>
public class ScreenStateReceivedEventArgs : EventArgs
{
    /// <summary>The id the reporting screen registered under.</summary>
    public required string ScreenId { get; init; }
    /// <summary>The report; one of the <see cref="ScreenStateBase"/> types.</summary>
    public required IScreenState State { get; init; }
}
