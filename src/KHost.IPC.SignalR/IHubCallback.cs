namespace KHost.IPC.SignalR;

internal interface IHubCallback
{
    void OnScreenDisconnected(string connectionId);

    /// <summary>Reserves a connection slot before the hub accepts it; false once the cap is hit.</summary>
    bool TryAcquireConnectionSlot(string connectionId);

    /// <summary>Idempotent: safe for a connection whose slot was never acquired, e.g. one rejected.</summary>
    void ReleaseConnectionSlot(string connectionId);

    /// <summary>Opens a session and returns the nonce the screen must sign every message with.</summary>
    string BeginSession(string connectionId);

    /// <summary>Verifies a signed registration and registers the screen on success.</summary>
    /// <remarks>False refuses it: no key, a bad MAC, a stale sequence, or the screen cap.</remarks>
    bool TryRegisterScreen(string connectionId, string? hostAddress, string envelopeJson);

    /// <summary>Verifies a signed state message and dispatches it on success.</summary>
    /// <remarks>False refuses it: not yet authenticated, a bad MAC, or a stale sequence.</remarks>
    bool TryAcceptState(string connectionId, string envelopeJson);

    /// <summary>Whether the connection finished the signed handshake, gating everything else.</summary>
    bool IsAuthenticated(string connectionId);
}
