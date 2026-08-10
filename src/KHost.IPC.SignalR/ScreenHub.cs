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

    public Task RegisterScreen(string screenId)
    {
        _callback.OnScreenConnected(screenId, Context.ConnectionId);
        return Task.CompletedTask;
    }

    public Task ReceiveState(string screenId, string stateJson)
    {
        var state = ScreenIpcSerializer.DeserializeState(stateJson);
        if (state is not null)
            _callback.OnStateReceived(screenId, state);
        return Task.CompletedTask;
    }
}
