namespace KHost.Abstractions.Services.IPC;

/// <summary>
/// One way of reaching screens. SignalR is a transport; a Cast sender is another. The rest of the
/// app talks to <see cref="IScreenServer"/>, which fans out over every transport, so nothing above
/// this line has to know which kind of screen it is addressing.
/// </summary>
public interface IScreenTransport
{
    event EventHandler<ScreenConnectionEventArgs>? ScreenConnected;
    event EventHandler<ScreenConnectionEventArgs>? ScreenDisconnected;
    event EventHandler<ScreenStateReceivedEventArgs>? StateReceived;

    IAsyncEnumerable<IScreenConnection> GetConnectedScreensAsync();

    /// <summary>
    /// Delivers a command, or returns false when the screen belongs to a different transport.
    /// Returning false rather than throwing is what lets the server try each one in turn.
    /// </summary>
    Task<bool> SendCommandAsync(string screenId, IScreenCommand command);
}
