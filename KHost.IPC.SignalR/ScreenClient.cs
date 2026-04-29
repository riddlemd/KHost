using KHost.Abstractions.Services.IPC;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace KHost.IPC.SignalR;

internal sealed class ScreenClient : IScreenClient, IAsyncDisposable
{
    private HubConnection? _connection;
    private ScreenClientState _state = ScreenClientState.Disconnected;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    public event EventHandler<ScreenCommandReceivedEventArgs>? CommandReceived;
    public event EventHandler<ScreenClientStateChangedEventArgs>? StateChanged;

    public string? ScreenId { get; private set; }

    public ScreenClientState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                var oldState = _state;
                _state = value;
                StateChanged?.Invoke(this, new ScreenClientStateChangedEventArgs
                {
                    OldState = oldState,
                    NewState = value
                });
            }
        }
    }

    public async Task ConnectAsync(string serverUri, string screenId, CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection != null && _state == ScreenClientState.Connected)
            {
                throw new InvalidOperationException("Already connected");
            }

            ScreenId = screenId;
            State = ScreenClientState.Connecting;

            _connection = new HubConnectionBuilder()
                .WithUrl(serverUri)
                .WithAutomaticReconnect()
                .Build();

            _connection.On<ScreenCommandBase>("ReceiveCommand", command =>
            {
                CommandReceived?.Invoke(this, new ScreenCommandReceivedEventArgs { Command = command });
            });

            _connection.Closed += async (error) =>
            {
                State = error != null ? ScreenClientState.Error : ScreenClientState.Disconnected;
            };

            _connection.Reconnecting += (error) =>
            {
                State = ScreenClientState.Reconnecting;
                return Task.CompletedTask;
            };

            _connection.Reconnected += (connectionId) =>
            {
                State = ScreenClientState.Connected;
                return Task.CompletedTask;
            };

            await _connection.StartAsync(cancellationToken);
            await _connection.InvokeAsync("RegisterScreen", screenId, cancellationToken);
            State = ScreenClientState.Connected;
        }
        catch (Exception)
        {
            State = ScreenClientState.Error;
            throw;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            if (_connection != null)
            {
                await _connection.StopAsync();
                await _connection.DisposeAsync();
                _connection = null;
                State = ScreenClientState.Disconnected;
                ScreenId = null;
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task SendStateAsync(IScreenState state)
    {
        if (_connection == null || State != ScreenClientState.Connected)
        {
            throw new InvalidOperationException("Not connected to server");
        }

        await _connection.InvokeAsync("ReceiveState", ScreenId, state);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _stateLock.Dispose();
    }
}
