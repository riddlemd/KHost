using KHost.Abstractions.Services.IPC;
using Microsoft.AspNetCore.SignalR;

namespace KHost.IPC.SignalR;

internal sealed class ScreenHub : Hub
{
    private readonly IHubCallback _callback;

    public ScreenHub(IHubCallback callback) => _callback = callback;

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _callback.OnScreenDisconnected(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public Task RegisterScreenAsync(string screenId, bool supportsSync, bool supportsAudio, bool supportsVideo)
    {
        _callback.OnScreenConnected(screenId, Context.ConnectionId, new ScreenCapabilities
        {
            SupportsSync = supportsSync,
            SupportsAudio = supportsAudio,
            SupportsVideo = supportsVideo,
        });

        return Task.CompletedTask;
    }

    /// <summary>Does nothing else: any work lands inside the round trip being measured.</summary>
    public long EchoClock() => DateTime.UtcNow.Ticks;

    public Task ReceiveStateAsync(string screenId, string stateJson)
    {
        var state = ScreenIpcSerializer.DeserializeState(stateJson);
        if (state is not null)
            _callback.OnStateReceived(screenId, state);
        return Task.CompletedTask;
    }
}
