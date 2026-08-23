namespace KHost.IPC.SignalR;

internal interface IHubCallback
{
    void OnScreenDisconnected(string connectionId);

    /// <summary>Reserves a connection slot before the hub accepts the connection; false once the concurrent-connection cap is reached.</summary>
    bool TryAcquireConnectionSlot(string connectionId);

    /// <summary>Idempotent: safe to call for a connection whose slot was never acquired (e.g. one rejected by <see cref="TryAcquireConnectionSlot"/>).</summary>
    void ReleaseConnectionSlot(string connectionId);

    /// <summary>Opens an authenticated session for a connection and returns the nonce the screen must sign every message with.</summary>
    string BeginSession(string connectionId);

    /// <summary>Verifies a signed registration and, on success, registers the screen. False refuses the connection: no key, a bad MAC, a stale sequence, or the screen cap.</summary>
    bool TryRegisterScreen(string connectionId, string? hostAddress, string envelopeJson);

    /// <summary>Verifies a signed state message and, on success, dispatches it. False refuses it: not yet authenticated, a bad MAC, or a stale sequence.</summary>
    bool TryAcceptState(string connectionId, string envelopeJson);

    /// <summary>Whether the connection has completed the signed handshake — anything a stranger could reach otherwise is gated on this.</summary>
    bool IsAuthenticated(string connectionId);
}
