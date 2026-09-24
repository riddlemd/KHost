using System.Diagnostics;
using KHost.Abstractions.Services.IPC;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace KHost.IPC.SignalR;

internal sealed class ScreenClient : IScreenClient, IAsyncDisposable
{
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long to wait before each attempt to win back a closed link, then this far apart.</summary>
    /// <remarks>A screen turned away because another holds the cap keeps asking at the slow end,
    /// rather than drawing a refusal from the host every second all night.</remarks>
    private static readonly TimeSpan[] ReconnectBackoff =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(15)];

    private HubConnection? _connection;
    private ScreenClientState _state = ScreenClientState.Disconnected;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    /// <summary>Held across numbering and sending one signed message.</summary>
    /// <remarks>The hub drops the link on a sequence that does not advance, so two sends must reach
    /// the wire in the order they were numbered in.</remarks>
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    /// <summary>Makes a session change and a signing one step, so nothing is signed against a new
    /// nonce as if it were already registered on it.</summary>
    private readonly Lock _signLock = new();

    private CancellationTokenSource _lifetime = new();
    private readonly ILogger<ScreenClient> _logger;

    private byte[]? _key;
    private ScreenCapabilities _capabilities = ScreenCapabilities.None;
    private string? _nonce;
    private long _outboundSeq;
    private long _inboundSeq;
    private bool _everRegistered;

    /// <summary>The connection id the current session registered on. The hub refuses, and drops the
    /// link over, state from a connection that has not registered, so state goes only on this one.</summary>
    /// <remarks>An id rather than a flag: SignalR clears <see cref="HubConnection.ConnectionId"/> the moment
    /// the transport is lost, well before its Reconnecting event reaches us.</remarks>
    private string? _registeredOn;

    /// <summary>A session was won and not given up, so a closed link is one to win back.</summary>
    private bool _established;

    /// <summary>1 while a reconnect loop runs; the loop is the only thing that restarts a closed link.</summary>
    private int _reconnecting;
    private TaskCompletionSource _sessionReady = NewSessionSignal();

    public event EventHandler<ScreenCommandReceivedEventArgs>? CommandReceived;
    public event EventHandler<ScreenClientStateChangedEventArgs>? StateChanged;

    public string? ScreenId { get; private set; }

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
        // the connection, so there is nothing to attempt. Checked here, before the lock, so a bad
        // call fails without disturbing a connection already under way.
        if (authKey is null) throw new InvalidOperationException("A screen auth key is required to connect.");

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection != null && _state == ScreenClientState.Connected)
            {
                throw new InvalidOperationException("Already connected");
            }

            // Assigned only once the "already connected" guard above has passed: a failed second
            // connect must not overwrite the key backing the live one.
            _key = authKey;

            // Cleared before the old connection goes, so its Closed does not start a reconnect.
            _established = false;
            if (_lifetime.IsCancellationRequested)
            {
                _lifetime.Dispose();
                _lifetime = new CancellationTokenSource();
            }

            ScreenId = screenId;
            _capabilities = capabilities ?? ScreenCapabilities.None;
            _everRegistered = false;
            _sessionReady = NewSessionSignal();
            State = ScreenClientState.Connecting;

            _logger.LogInformation("Connecting to {Url}", serverUri);

            // A failed attempt leaves its connection behind, and a caller retrying the first
            // connect would abandon one per attempt.
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            var connection = _connection = new HubConnectionBuilder()
                .WithUrl(serverUri)
                .WithAutomaticReconnect()
                .Build();

            // Resetting the sequences on a new nonce is what makes a reconnect a fresh session
            // rather than one the host rejects for repeating the old one.
            _connection.On<string>("Session", nonce =>
            {
                lock (_signLock)
                {
                    _nonce = nonce;
                    Interlocked.Exchange(ref _outboundSeq, 0);
                }

                _inboundSeq = 0;

                if (_everRegistered)
                    _ = ReregisterAfterReconnectAsync();
                else
                    _sessionReady.TrySetResult();
            });

            _connection.On<string>("ReceiveCommand", OnCommandEnvelope);

            _connection.Closed += error => OnClosedAsync(connection, error);

            _connection.Reconnecting += (error) =>
            {
                _logger.LogWarning(error, "SignalR reconnecting");
                State = ScreenClientState.Reconnecting;
                return Task.CompletedTask;
            };

            // Stays Reconnecting: Connected waits for the re-register, which the Session handler starts.
            _connection.Reconnected += (connectionId) =>
            {
                _logger.LogInformation("SignalR reconnected (connectionId={ConnectionId})", connectionId);
                return Task.CompletedTask;
            };

            await StartSessionAsync(connection, cancellationToken);
            _established = true;

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

        // Before the lock: a reconnect attempt holding it gives up rather than being waited out.
        _lifetime.Cancel();

        await _stateLock.WaitAsync();
        try
        {
            _established = false;

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

    /// <summary>NTP's estimator: keeps the shortest round trip, so one slow probe can't skew it.</summary>
    public async Task<TimeSpan> EstimateClockOffsetAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is null || State != ScreenClientState.Connected)
            throw new InvalidOperationException("Not connected to server");

        var bestRoundTrip = TimeSpan.MaxValue;
        var offset = TimeSpan.Zero;

        for (var i = 0; i < 5; i++)
        {
            var sentAt = DateTime.UtcNow;
            // Stopwatch, not a second UtcNow: the round trip is an elapsed duration, and the
            // monotonic clock doesn't get skewed by whatever NTP does to the wall clock mid-probe.
            var sentTimestamp = Stopwatch.GetTimestamp();
            var hostTicks = await _connection.InvokeAsync<long>(nameof(ScreenHub.EchoClock), cancellationToken);
            var roundTrip = Stopwatch.GetElapsedTime(sentTimestamp);

            if (roundTrip >= bestRoundTrip) continue;

            bestRoundTrip = roundTrip;
            offset = new DateTime(hostTicks, DateTimeKind.Utc) - (sentAt + roundTrip / 2);
        }

        _logger.LogInformation("Clock offset to host: {Offset} (best round trip {RoundTrip})", offset, bestRoundTrip);

        return offset;
    }

    /// <summary>Skipped, not thrown, while this screen is not registered on a live session.</summary>
    public async Task SendStateAsync(IScreenState state)
    {
        if (_connection is not { } connection)
        {
            _logger.LogDebug("SendStateAsync dropped (not connected)");
            return;
        }

        await SendSignedAsync(
            connection, nameof(ScreenHub.ReceiveStateAsync), ScreenIpcSerializer.SerializeState(state), requiresRegistration: true);
    }

    /// <summary>Opens <paramref name="connection"/> and registers on it, throwing when the host refuses.</summary>
    private async Task StartSessionAsync(HubConnection connection, CancellationToken cancellationToken)
    {
        _everRegistered = false;
        _sessionReady = NewSessionSignal();

        await connection.StartAsync(cancellationToken);

        // The host sends the nonce right after the connection opens; the handshake cannot start
        // until it arrives.
        await _sessionReady.Task.WaitAsync(SessionTimeout, cancellationToken);

        // Throws when the hub refuses, since it aborts the connection the call is waiting on.
        await SendRegisterAsync();

        _everRegistered = true;
    }

    private Task OnClosedAsync(HubConnection connection, Exception? error)
    {
        if (error != null)
            _logger.LogError(error, "SignalR connection closed with error");
        else
            _logger.LogInformation("SignalR connection closed");

        // A connection already replaced says nothing about the current one.
        if (!ReferenceEquals(connection, _connection)) return Task.CompletedTask;

        // A hub-side Abort closes without allowing SignalR's own reconnect, so a link that was up
        // is won back here. A first connect that failed is its caller's to retry.
        if (!_established || _lifetime.IsCancellationRequested)
        {
            State = error is null ? ScreenClientState.Disconnected : ScreenClientState.Error;
            return Task.CompletedTask;
        }

        State = ScreenClientState.Reconnecting;

        if (Interlocked.CompareExchange(ref _reconnecting, 1, 0) == 0)
            _ = ReconnectAsync(connection, _lifetime.Token);

        return Task.CompletedTask;
    }

    /// <summary>Restarts a closed connection and re-registers on it until that works or the client stops.</summary>
    private async Task ReconnectAsync(HubConnection connection, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await Task.Delay(ReconnectBackoff[Math.Min(attempt, ReconnectBackoff.Length - 1)], cancellationToken);

                await _stateLock.WaitAsync(cancellationToken);
                try
                {
                    if (!ReferenceEquals(connection, _connection) || !_established) break;

                    // A failed attempt can leave the connection open but unregistered.
                    if (connection.State != HubConnectionState.Disconnected)
                        await connection.StopAsync(cancellationToken);

                    await StartSessionAsync(connection, cancellationToken);

                    _logger.LogInformation("Reconnected to the host on attempt {Attempt}", attempt + 1);
                    State = ScreenClientState.Connected;
                }
                finally
                {
                    _stateLock.Release();
                }
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                // The first failure carries the stack; a host that stays away would otherwise fill the log.
                if (attempt == 0)
                    _logger.LogWarning(ex, "Could not reconnect to the host; retrying");
                else
                    _logger.LogDebug("Still could not reconnect (attempt {Attempt}): {Message}", attempt + 1, ex.Message);

                continue;
            }

            Interlocked.Exchange(ref _reconnecting, 0);

            // A close that landed while this loop held the flag was ignored by OnClosedAsync.
            if (connection.State == HubConnectionState.Disconnected
                && ReferenceEquals(connection, _connection)
                && !cancellationToken.IsCancellationRequested
                && Interlocked.CompareExchange(ref _reconnecting, 1, 0) == 0)
            {
                State = ScreenClientState.Reconnecting;
                continue;
            }

            return;
        }

        Interlocked.Exchange(ref _reconnecting, 0);
    }

    // Fires from the Session handler with nothing awaiting it, so a failure must be caught here. A
    // connection left up but unregistered never recovers; stopping it hands it to OnClosedAsync's loop.
    private async Task ReregisterAfterReconnectAsync()
    {
        try
        {
            await SendRegisterAsync();
            State = ScreenClientState.Connected;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Re-register after reconnect failed for {ScreenId}", ScreenId);

            try
            {
                if (_connection is { } connection) await connection.StopAsync();
            }
            catch (Exception stopEx)
            {
                _logger.LogDebug(stopEx, "Could not stop the unregistered connection");
            }
        }
    }

    private async Task SendRegisterAsync()
    {
        if (_connection is null) return;

        _logger.LogInformation(
            "RegisterScreen sent for {ScreenId} (audio={SupportsAudio} video={SupportsVideo})",
            ScreenId, _capabilities.SupportsAudio, _capabilities.SupportsVideo);

        var connection = _connection;

        await SendSignedAsync(
            connection, nameof(ScreenHub.RegisterScreenAsync), RegisterPayload.From(_capabilities).ToJson(), requiresRegistration: false);

        lock (_signLock) { _registeredOn = connection.ConnectionId; }
    }

    private async Task SendSignedAsync(HubConnection connection, string method, string payload, bool requiresRegistration)
    {
        await _sendGate.WaitAsync();
        try
        {
            SignedEnvelope envelope;

            lock (_signLock)
            {
                if (requiresRegistration && (_registeredOn is null || _registeredOn != connection.ConnectionId))
                {
                    _logger.LogDebug("{Method} skipped: not registered on the current session", method);
                    return;
                }

                envelope = Sign(payload);
            }

            await connection.InvokeAsync(method, envelope.ToJson());
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private void OnCommandEnvelope(string envelopeJson)
    {
        var envelope = SignedEnvelope.TryParse(envelopeJson);
        if (envelope is null || _key is null || _nonce is null) return;

        // Logged apart: a bad MAC is a forgery or wrong key; a stale sequence is usually our own
        // command overtaken in flight. One message for both sends an ordering bug looking for an attacker.
        if (!ScreenMessageAuth.Verify(_key, _nonce, envelope.Seq, envelope.Payload, envelope.Mac))
        {
            _logger.LogWarning("Dropped a command whose signature did not verify (seq {Seq})", envelope.Seq);
            return;
        }

        if (envelope.Seq <= _inboundSeq)
        {
            _logger.LogWarning(
                "Dropped a command that did not advance the sequence (seq {Seq}, last accepted {Accepted}) "
                    + "— a replay, or one overtaken on the way here",
                envelope.Seq, _inboundSeq);
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
        _sendGate.Dispose();
        _lifetime.Dispose();
    }
}
