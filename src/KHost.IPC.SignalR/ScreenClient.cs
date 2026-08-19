using KHost.Abstractions.Services.IPC;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace KHost.IPC.SignalR;

internal sealed class ScreenClient : IScreenClient, IAsyncDisposable
{
    private HubConnection? _connection;
    private ScreenClientState _state = ScreenClientState.Disconnected;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly ILogger<ScreenClient> _logger;

    public event EventHandler<ScreenCommandReceivedEventArgs>? CommandReceived;
    public event EventHandler<ScreenClientStateChangedEventArgs>? StateChanged;

    public string? ScreenId { get; private set; }

    public ScreenClient() : this(null)
    {
    }

    public ScreenClient(ILoggerFactory? loggerFactory)
    {
        _logger = (loggerFactory ?? new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory())
            .CreateLogger<ScreenClient>();
    }

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

    public async Task ConnectAsync(
        string serverUri,
        string screenId,
        ScreenCapabilities? capabilities = null,
        CancellationToken cancellationToken = default)
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

            _logger.LogInformation("Connecting to {Url}", serverUri);

            _connection = new HubConnectionBuilder()
                .WithUrl(serverUri)
                .WithAutomaticReconnect()
                .Build();

            _connection.On<string>("ReceiveCommand", commandJson =>
            {
                var command = ScreenIpcSerializer.DeserializeCommand(commandJson);
                if (command is not null)
                    CommandReceived?.Invoke(this, new ScreenCommandReceivedEventArgs { Command = command });
            });

            _connection.Closed += async (error) =>
            {
                if (error != null)
                {
                    _logger.LogError(error, "SignalR connection closed with error");
                    State = ScreenClientState.Error;
                }
                else
                {
                    _logger.LogInformation("SignalR connection closed");
                    State = ScreenClientState.Disconnected;
                }
            };

            _connection.Reconnecting += (error) =>
            {
                _logger.LogWarning(error, "SignalR reconnecting");
                State = ScreenClientState.Reconnecting;
                return Task.CompletedTask;
            };

            _connection.Reconnected += (connectionId) =>
            {
                _logger.LogInformation("SignalR reconnected (connectionId={ConnectionId})", connectionId);
                State = ScreenClientState.Connected;
                return Task.CompletedTask;
            };

            await _connection.StartAsync(cancellationToken);

            var declared = capabilities ?? ScreenCapabilities.None;
            _logger.LogInformation(
                "RegisterScreen sent for {ScreenId} (sync={SupportsSync} audio={SupportsAudio} video={SupportsVideo})",
                screenId, declared.SupportsSync, declared.SupportsAudio, declared.SupportsVideo);

            await _connection.InvokeAsync(
                nameof(ScreenHub.RegisterScreenAsync),
                screenId, declared.SupportsSync, declared.SupportsAudio, declared.SupportsVideo, cancellationToken);

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
        _logger.LogInformation("Disconnecting");
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

    /// <summary>
    /// NTP's estimator: keep the shortest round trip, whose "half of it" assumption is least
    /// wrong. Averaging lets one delayed probe drag the estimate off.
    /// </summary>
    public async Task<TimeSpan> EstimateClockOffsetAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is null || State != ScreenClientState.Connected)
            throw new InvalidOperationException("Not connected to server");

        var bestRoundTrip = TimeSpan.MaxValue;
        var offset = TimeSpan.Zero;

        for (var i = 0; i < 5; i++)
        {
            var sentAt = DateTime.UtcNow;
            var hostTicks = await _connection.InvokeAsync<long>(nameof(ScreenHub.EchoClock), cancellationToken);
            var receivedAt = DateTime.UtcNow;

            var roundTrip = receivedAt - sentAt;
            if (roundTrip >= bestRoundTrip) continue;

            bestRoundTrip = roundTrip;
            offset = new DateTime(hostTicks, DateTimeKind.Utc) - (sentAt + roundTrip / 2);
        }

        _logger.LogInformation("Clock offset to host: {Offset} (best round trip {RoundTrip})", offset, bestRoundTrip);

        return offset;
    }

    public async Task SendStateAsync(IScreenState state)
    {
        if (_connection == null || State != ScreenClientState.Connected)
        {
            _logger.LogDebug("SendStateAsync dropped (not connected)");
            throw new InvalidOperationException("Not connected to server");
        }

        await _connection.InvokeAsync(nameof(ScreenHub.ReceiveStateAsync), ScreenId, ScreenIpcSerializer.SerializeState(state));
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _stateLock.Dispose();
    }
}
