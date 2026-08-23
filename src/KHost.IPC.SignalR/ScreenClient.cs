using KHost.Abstractions.Services.IPC;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace KHost.IPC.SignalR;

internal sealed class ScreenClient : IScreenClient, IAsyncDisposable
{
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(10);

    private HubConnection? _connection;
    private ScreenClientState _state = ScreenClientState.Disconnected;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly ILogger<ScreenClient> _logger;

    private byte[]? _key;
    private ScreenCapabilities _capabilities = ScreenCapabilities.None;
    private string? _nonce;
    private long _outboundSeq;
    private long _inboundSeq;
    private bool _everRegistered;
    private TaskCompletionSource _sessionReady = NewSessionSignal();

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
        byte[]? authKey = null,
        CancellationToken cancellationToken = default)
    {
        // The key is what proves this screen is one the host provisioned; without it the hub refuses
        // the connection, so there is nothing to attempt.
        _key = authKey ?? throw new InvalidOperationException("A screen auth key is required to connect.");

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection != null && _state == ScreenClientState.Connected)
            {
                throw new InvalidOperationException("Already connected");
            }

            ScreenId = screenId;
            _capabilities = capabilities ?? ScreenCapabilities.None;
            _everRegistered = false;
            _sessionReady = NewSessionSignal();
            State = ScreenClientState.Connecting;

            _logger.LogInformation("Connecting to {Url}", serverUri);

            _connection = new HubConnectionBuilder()
                .WithUrl(serverUri)
                .WithAutomaticReconnect()
                .Build();

            // The nonce arrives before anything is registered. Resetting the sequences here is what
            // makes a reconnect a fresh session: the host issues a new nonce on the new connection,
            // and a re-register re-establishes the screen (which the old client never did).
            _connection.On<string>("Session", nonce =>
            {
                _nonce = nonce;
                Interlocked.Exchange(ref _outboundSeq, 0);
                _inboundSeq = 0;

                if (_everRegistered)
                    _ = SendRegisterAsync();
                else
                    _sessionReady.TrySetResult();
            });

            _connection.On<string>("ReceiveCommand", OnCommandEnvelope);

            _connection.Closed += (error) =>
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

                return Task.CompletedTask;
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

            // The host sends the nonce right after the connection opens; the handshake cannot start
            // until it arrives.
            await _sessionReady.Task.WaitAsync(SessionTimeout, cancellationToken);

            await SendRegisterAsync();
            _everRegistered = true;

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

        await _connection.InvokeAsync(
            nameof(ScreenHub.ReceiveStateAsync), Sign(ScreenIpcSerializer.SerializeState(state)).ToJson());
    }

    private async Task SendRegisterAsync()
    {
        if (_connection is null) return;

        _logger.LogInformation(
            "RegisterScreen sent for {ScreenId} (sync={SupportsSync} audio={SupportsAudio} video={SupportsVideo})",
            ScreenId, _capabilities.SupportsSync, _capabilities.SupportsAudio, _capabilities.SupportsVideo);

        await _connection.InvokeAsync(
            nameof(ScreenHub.RegisterScreenAsync), Sign(RegisterPayload.From(_capabilities).ToJson()).ToJson());
    }

    private void OnCommandEnvelope(string envelopeJson)
    {
        var envelope = SignedEnvelope.TryParse(envelopeJson);
        if (envelope is null || _key is null || _nonce is null) return;

        // A command whose MAC does not verify, or whose sequence does not advance, is a forgery or a
        // replay — dropped, never dispatched to the player.
        if (!ScreenMessageAuth.Verify(_key, _nonce, envelope.Seq, envelope.Payload, envelope.Mac)
            || envelope.Seq <= _inboundSeq)
        {
            _logger.LogWarning("Dropped a command that did not verify (seq {Seq})", envelope.Seq);
            return;
        }

        _inboundSeq = envelope.Seq;

        var command = ScreenIpcSerializer.DeserializeCommand(envelope.Payload);
        if (command is not null)
            CommandReceived?.Invoke(this, new ScreenCommandReceivedEventArgs { Command = command });
    }

    private SignedEnvelope Sign(string payload)
    {
        var nonce = _nonce ?? throw new InvalidOperationException("No session nonce; the handshake has not completed.");
        var key = _key ?? throw new InvalidOperationException("No auth key.");
        var seq = Interlocked.Increment(ref _outboundSeq);

        return new SignedEnvelope(ScreenId ?? "", seq, payload, ScreenMessageAuth.Sign(key, nonce, seq, payload));
    }

    // RunContinuationsAsynchronously: the Session handler runs on the connection's dispatch loop, so
    // completing the waiter must not run ConnectAsync's continuation there and stall the loop.
    private static TaskCompletionSource NewSessionSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _stateLock.Dispose();
    }
}
