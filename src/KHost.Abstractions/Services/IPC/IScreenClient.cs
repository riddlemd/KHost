namespace KHost.Abstractions.Services.IPC;

/// <summary>The LocalScreen app's end of the link to the host: receives commands, reports state.</summary>
/// <remarks>
/// <para>Host-only. This is the wire contract between the host and its own LocalScreen app, used
/// only inside that app. A plugin has no business with it: a plugin display reaches its own device
/// through <see cref="IDisplayProvider"/>.</para>
/// <para>Once connected, a dropped link is won back on its own, with the state passing through
/// <see cref="ScreenClientState.Reconnecting"/>, until <see cref="DisconnectAsync"/> is called.
/// A first <see cref="ConnectAsync"/> that fails is the caller's to retry.</para>
/// <para>The events are raised on the connection's own thread; marshal to a UI thread before
/// touching one.</para>
/// </remarks>
public interface IScreenClient
{
    /// <summary>A command arrived from the host and passed authentication; one that does not is
    /// dropped without being raised.</summary>
    event EventHandler<ScreenCommandReceivedEventArgs>? CommandReceived;

    /// <summary><see cref="State"/> changed.</summary>
    event EventHandler<ScreenClientStateChangedEventArgs>? StateChanged;

    /// <summary>The id passed to <see cref="ConnectAsync"/>; null before it and after
    /// <see cref="DisconnectAsync"/>.</summary>
    string? ScreenId { get; }

    /// <summary>Where the link stands; <see cref="ScreenClientState.Connected"/> only once the host has
    /// accepted the registration.</summary>
    ScreenClientState State { get; }

    /// <summary>Connects to the host and registers as <paramref name="screenId"/>, completing once the
    /// host has accepted it.</summary>
    /// <param name="serverUri">The host's screen endpoint.</param>
    /// <param name="screenId">The id this screen registers under; the host's key for it.</param>
    /// <param name="capabilities">What this screen can play; null declares
    /// <see cref="ScreenCapabilities.None"/>.</param>
    /// <param name="authKey">The key the host provisioned for this screen id. Required despite the
    /// default.</param>
    /// <param name="cancellationToken">Abandons the connect.</param>
    /// <remarks>Also throws when the host refuses the registration or cannot be reached, leaving
    /// <see cref="State"/> at <see cref="ScreenClientState.Error"/>.</remarks>
    /// <exception cref="InvalidOperationException"><paramref name="authKey"/> is null, or the client
    /// is already connected.</exception>
    Task ConnectAsync(
        string serverUri,
        string screenId,
        ScreenCapabilities? capabilities = null,
        byte[]? authKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>Closes the link and stops any reconnect in progress.</summary>
    Task DisconnectAsync();

    /// <summary>Skipped, not thrown, while the screen is not registered on a live session.</summary>
    /// <param name="state">One of the <see cref="ScreenStateBase"/> types.</param>
    Task SendStateAsync(IScreenState state);

    /// <summary>Estimates how far the host's clock is from this machine's, for a scheduled start.</summary>
    /// <returns>Host time minus local time: add it to a local UTC time to get the host's.</returns>
    /// <exception cref="InvalidOperationException">Not <see cref="ScreenClientState.Connected"/>.</exception>
    Task<TimeSpan> EstimateClockOffsetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Carries one command for <see cref="IScreenClient.CommandReceived"/>.</summary>
public class ScreenCommandReceivedEventArgs : EventArgs
{
    /// <summary>The command the host sent, already authenticated.</summary>
    public required IScreenCommand Command { get; init; }
}

/// <summary>Carries one move for <see cref="IScreenClient.StateChanged"/>.</summary>
public class ScreenClientStateChangedEventArgs : EventArgs
{
    /// <summary>The state before the change.</summary>
    public required ScreenClientState OldState { get; init; }
    /// <summary>The state now; the same value <see cref="IScreenClient.State"/> reads.</summary>
    public required ScreenClientState NewState { get; init; }
}

/// <summary>Where a screen's link to the host stands.</summary>
public enum ScreenClientState
{
    /// <summary>No link: not yet connected, or closed on request.</summary>
    Disconnected,

    /// <summary>A first <see cref="IScreenClient.ConnectAsync"/> is in progress.</summary>
    Connecting,

    /// <summary>Linked, and the host has accepted the registration; commands flow.</summary>
    Connected,

    /// <summary>An established link dropped and is being won back on its own; no commands arrive
    /// meanwhile.</summary>
    Reconnecting,

    /// <summary>A connect failed or a link that never came up closed with an error; nothing is
    /// retried until the caller connects again.</summary>
    Error
}
