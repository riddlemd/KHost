using KHost.Abstractions.Services.IPC;

namespace KHost.IPC.SignalR;

internal interface IHubCallback
{
    void OnScreenConnected(string screenId, string connectionId, string? hostAddress, ScreenCapabilities capabilities);
    void OnScreenDisconnected(string connectionId);
    void OnStateReceived(string screenId, IScreenState state);

    /// <summary>Reserves a connection slot before the hub accepts the connection; false once the concurrent-connection cap is reached.</summary>
    bool TryAcquireConnectionSlot(string connectionId);

    /// <summary>Idempotent: safe to call for a connection whose slot was never acquired (e.g. one rejected by <see cref="TryAcquireConnectionSlot"/>).</summary>
    void ReleaseConnectionSlot(string connectionId);
}
