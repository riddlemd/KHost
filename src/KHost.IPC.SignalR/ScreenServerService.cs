using System.Security.Cryptography;
using KHost.IPC.SignalR.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.IPC.SignalR;

internal sealed class ScreenServerService : IScreenServer, IHubCallback
{
    public sealed class ServiceOptions
    {
        public const string SectionName = "ScreenServer";

        /// <summary>Caps live hub connections before a screen registers, to bound a LAN flood.</summary>
        public int MaxConcurrentConnections { get; set; } = 20;
    }

    /// <summary>Caps registered screens apart from the connection cap; reusing an id isn't new.</summary>
    /// <remarks>The host drives one display at a time, and a hand-launched second screen would
    /// otherwise simply join and be sent a song it was never chosen for.</remarks>
    private const int MaxRegisteredScreens = 1;

    private readonly IHubContext<ScreenHub> _hubContext;
    private readonly IScreenKeyStore _keyStore;
    private readonly ServiceOptions _options;
    private readonly ILogger<ScreenServerService>? _logger;
    private readonly Dictionary<string, ScreenConnection> _connections = [];
    private readonly HashSet<string> _liveConnectionIds = [];
    private readonly Dictionary<string, SessionAuth> _sessions = [];

    /// <summary>Guards every dictionary above. None of the sections it wraps await, so a plain
    /// non-reentrant lock is enough; the per-session <see cref="SessionAuth.SendGate"/> is the one
    /// that still needs a <see cref="SemaphoreSlim"/>, because it wraps the actual send.</summary>
    private readonly Lock _lock = new();

    public event EventHandler<ScreenConnectionEventArgs>? ScreenConnected;
    public event EventHandler<ScreenConnectionEventArgs>? ScreenDisconnected;
    public event EventHandler<ScreenStateReceivedEventArgs>? StateReceived;

    public ScreenServerService(
        IHubContext<ScreenHub> hubContext,
        IScreenKeyStore keyStore,
        IOptions<ServiceOptions>? options = null,
        ILogger<ScreenServerService>? logger = null)
    {
        _hubContext = hubContext;
        _keyStore = keyStore;
        _options = options?.Value ?? new ServiceOptions();
        _logger = logger;
    }

    bool IHubCallback.TryAcquireConnectionSlot(string connectionId)
    {
        lock (_lock)
        {
            if (_liveConnectionIds.Count >= _options.MaxConcurrentConnections) return false;
            return _liveConnectionIds.Add(connectionId);
        }
    }

    void IHubCallback.ReleaseConnectionSlot(string connectionId)
    {
        lock (_lock) { _liveConnectionIds.Remove(connectionId); }
    }

    string IHubCallback.BeginSession(string connectionId)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        lock (_lock) { _sessions[connectionId] = new SessionAuth { Nonce = nonce }; }

        return nonce;
    }

    bool IHubCallback.TryRegisterScreen(string connectionId, string? hostAddress, string envelopeJson)
    {
        var envelope = SignedEnvelope.TryParse(envelopeJson);
        if (envelope is null) return false;

        var key = _keyStore.GetKey(envelope.ScreenId);
        if (key is null)
        {
            _logger?.LogWarning("Refused registration for '{ScreenId}': no key is provisioned for it", envelope.ScreenId);
            return false;
        }

        ScreenConnection conn;

        lock (_lock)
        {
            // Every refusal below says why. A screen the host turned away shows "Lost the host"
            // and waits, which from the room looks exactly like a screen that crashed.
            if (!_sessions.TryGetValue(connectionId, out var session))
            {
                _logger?.LogWarning(
                    "Refused registration for '{ScreenId}': no session was begun for {ConnectionId}",
                    envelope.ScreenId, connectionId);

                return false;
            }

            if (!ScreenMessageAuth.Verify(key, session.Nonce, envelope.Seq, envelope.Payload, envelope.Mac))
            {
                _logger?.LogWarning(
                    "Refused registration for '{ScreenId}': the signature did not verify",
                    envelope.ScreenId);

                return false;
            }

            if (envelope.Seq <= session.ExpectedInboundSeq)
            {
                _logger?.LogWarning(
                    "Refused registration for '{ScreenId}': sequence {Seq} is not past {Expected}",
                    envelope.ScreenId, envelope.Seq, session.ExpectedInboundSeq);

                return false;
            }

            var payload = RegisterPayload.TryParse(envelope.Payload);
            if (payload is null)
            {
                _logger?.LogWarning(
                    "Refused registration for '{ScreenId}': its payload could not be read",
                    envelope.ScreenId);

                return false;
            }

            // A re-registration under an existing id overwrites in place; it doesn't count against the cap.
            if (!_connections.ContainsKey(envelope.ScreenId) && _connections.Count >= MaxRegisteredScreens)
            {
                // Logged, not silent: a refused screen shows "Lost the host" and waits, which looks
                // identical to a crash from the operator's side of the room.
                _logger?.LogWarning(
                    "Refused registration for '{ScreenId}': {Count} of {Max} screens are already registered",
                    envelope.ScreenId, _connections.Count, MaxRegisteredScreens);

                return false;
            }

            session.Key = key;
            session.ScreenId = envelope.ScreenId;
            session.ExpectedInboundSeq = envelope.Seq;

            conn = new ScreenConnection
            {
                ScreenId = envelope.ScreenId,
                ConnectionId = connectionId,
                ConnectedAt = DateTime.UtcNow,
                HostAddress = hostAddress,
                Capabilities = payload.ToCapabilities(),
            };
            _connections[envelope.ScreenId] = conn;
        }

        // Raised outside the lock, the same as TryAcceptState raises StateReceived: a subscriber
        // that calls back in (e.g. BroadcastCommandAsync) would otherwise re-enter this non-reentrant lock.
        ScreenConnected?.Invoke(this, new ScreenConnectionEventArgs { Connection = conn });
        return true;
    }

    bool IHubCallback.TryAcceptState(string connectionId, string envelopeJson)
    {
        var envelope = SignedEnvelope.TryParse(envelopeJson);
        if (envelope is null) return false;

        IScreenState? state;

        lock (_lock)
        {
            // Every refusal says why: the hub drops the link on any of them, and the screen then
            // shows "Lost the host".
            if (!_sessions.TryGetValue(connectionId, out var session) || session.Key is null)
            {
                _logger?.LogWarning(
                    "Refused state from '{ScreenId}': {ConnectionId} has not registered", envelope.ScreenId, connectionId);

                return false;
            }

            if (envelope.ScreenId != session.ScreenId)
            {
                _logger?.LogWarning(
                    "Refused state from '{ScreenId}': {ConnectionId} registered as '{Registered}'",
                    envelope.ScreenId, connectionId, session.ScreenId);

                return false;
            }

            if (!ScreenMessageAuth.Verify(session.Key, session.Nonce, envelope.Seq, envelope.Payload, envelope.Mac))
            {
                _logger?.LogWarning("Refused state from '{ScreenId}': the signature did not verify", envelope.ScreenId);

                return false;
            }

            if (envelope.Seq <= session.ExpectedInboundSeq)
            {
                _logger?.LogWarning(
                    "Refused state from '{ScreenId}': sequence {Seq} is not past {Expected}",
                    envelope.ScreenId, envelope.Seq, session.ExpectedInboundSeq);

                return false;
            }

            state = ScreenIpcSerializer.DeserializeState(envelope.Payload);
            if (state is null)
            {
                _logger?.LogWarning("Refused state from '{ScreenId}': its payload could not be read", envelope.ScreenId);

                return false;
            }

            session.ExpectedInboundSeq = envelope.Seq;
        }

        // Raised outside the lock: a handler that enumerates connected screens must not deadlock on it.
        StateReceived?.Invoke(this, new ScreenStateReceivedEventArgs { ScreenId = envelope.ScreenId, State = state });
        return true;
    }

    bool IHubCallback.IsAuthenticated(string connectionId)
    {
        lock (_lock) { return _sessions.TryGetValue(connectionId, out var session) && session.Key is not null; }
    }

    void IHubCallback.OnScreenDisconnected(string connectionId)
    {
        ScreenConnection? conn = null;

        lock (_lock)
        {
            // Null the key, not just remove the session: SendToAsync holds its own reference to this
            // object and re-checks Key under the send gate, so only nulling it there reaches that copy.
            if (_sessions.Remove(connectionId, out var session))
                session.Key = null;

            conn = _connections.Values.FirstOrDefault(c => c.ConnectionId == connectionId);
            if (conn is not null) _connections.Remove(conn.ScreenId);
        }

        if (conn is null) return;

        // Raised outside the lock, for the same reason TryRegisterScreen raises ScreenConnected there.
        ScreenDisconnected?.Invoke(this, new ScreenConnectionEventArgs { Connection = conn });
    }

    public async IAsyncEnumerable<IScreenConnection> GetConnectedScreensAsync()
    {
        List<IScreenConnection> snapshot;
        lock (_lock) { snapshot = [.. _connections.Values]; }

        foreach (var conn in snapshot)
            yield return conn;
    }

    public async Task BroadcastCommandAsync(IScreenCommand command)
    {
        // Every command is signed with the screen's own key, so it goes to that connection alone,
        // never Clients.All, which would hand it to connections that have not registered.
        List<ScreenConnection> snapshot;
        lock (_lock) { snapshot = [.. _connections.Values]; }

        await Task.WhenAll(snapshot.Select(conn => SendToAsync(conn, command)));
    }

    private async Task SendToAsync(ScreenConnection connection, IScreenCommand command)
    {
        var payload = ScreenIpcSerializer.SerializeCommand(Reachable(command, connection));

        // Only a screen that finished the handshake has a key; one still registering is skipped
        // rather than sent an unsigned command it would reject anyway.
        SessionAuth? session = null;
        lock (_lock)
        {
            if (_sessions.TryGetValue(connection.ConnectionId, out var found) && found.Key is not null)
                session = found;
        }

        if (session is null) return;

        // Numbering and delivery are one step: splitting them lets two commands overtake and lose the loser.
        // Held per screen, not shared, so one slow screen doesn't block anybody else's queue.
        await session.SendGate.WaitAsync();
        try
        {
            // Re-read under the gate: a queued command may have outlived its session.
            if (session.Key is not { } key) return;

            var seq = ++session.OutboundSeq;

            var envelope = new SignedEnvelope(
                connection.ScreenId, seq, payload, ScreenMessageAuth.Sign(key, session.Nonce, seq, payload));

            await _hubContext.Clients.Client(connection.ConnectionId).SendAsync("ReceiveCommand", envelope.ToJson());
        }
        finally { session.SendGate.Release(); }
    }

    private IScreenCommand Reachable(IScreenCommand command, ScreenConnection connection)
    {
        if (command is not LoadMediaCommand load) return command;

        var url = StreamUrlRewriter.ForScreen(load.StreamUrl, connection.HostAddress);
        if (ReferenceEquals(url, load.StreamUrl)) return command;

        // Worth a line: when a screen plays nothing, the first question is which address it was
        // told to fetch from.
        _logger?.LogInformation("Screen {ScreenId} will fetch the stream from {Url}", connection.ScreenId, url);

        return WithStreamUrl(load, url);
    }

    /// <summary>The same load, pointed at an address the screen can actually reach.</summary>
    /// <remarks>Every property is carried, not only the one being changed. Rebuilt by hand because
    /// the command is a class and cannot be <c>with</c>-ed, which is how <c>Tempo</c> came to be
    /// silently dropped here for any screen reached on a non-loopback address: the stream was
    /// retimed and the words, which scale by it, drifted away from it.
    ///
    /// <para>Internal so <c>LoadMediaCommandRewriteTests</c> can hold it to that by reflection —
    /// the next property added to the command fails that test rather than going missing.</para>
    /// </remarks>
    internal static LoadMediaCommand WithStreamUrl(LoadMediaCommand load, string? streamUrl) => new()
    {
        StreamUrl = streamUrl,
        StreamStartOffset = load.StreamStartOffset,
        Tempo = load.Tempo,
        Stems = load.Stems,
    };

    private sealed class SessionAuth
    {
        public required string Nonce { get; init; }
        public byte[]? Key { get; set; }
        public string? ScreenId { get; set; }
        public long ExpectedInboundSeq { get; set; }
        public long OutboundSeq { get; set; }

        /// <summary>This screen's send gate: keeps a sequence number and its send from splitting.</summary>
        /// <remarks>Per session, so a slow screen holds up only its own queue.</remarks>
        public SemaphoreSlim SendGate { get; } = new(1, 1);
    }

    private sealed class ScreenConnection : IScreenConnection
    {
        public required string ScreenId { get; init; }
        public required string ConnectionId { get; init; }
        public DateTime ConnectedAt { get; init; }
        public string? HostAddress { get; init; }
        public bool IsConnected => true;
        public ScreenCapabilities Capabilities { get; init; } = ScreenCapabilities.None;
    }
}
