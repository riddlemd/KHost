using KHost.Abstractions.Services.IPC;

namespace KHost.IPC.SignalR;

internal interface IHubCallback
{
    void OnScreenConnected(string screenId, string connectionId, ScreenCapabilities capabilities);
    void OnScreenDisconnected(string connectionId);
    void OnStateReceived(string screenId, IScreenState state);
}
