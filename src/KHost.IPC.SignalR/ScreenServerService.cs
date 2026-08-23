using System.Security.Cryptography;
using KHost.Abstractions.Services.IPC;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.IPC.SignalR;

internal sealed class ScreenServerService : IScreenServer, IHubCallback
{
    public sealed class ServiceOptions
    {
        public const string SectionName = "ScreenServer";

        /// <summary>Caps live hub connections regardless of whether they ever register a screen — bounds an unauthenticated LAN flood.</summary>
        public int MaxConcurrentConnections { get; set; } = 20;

        /// <summary>Caps registered screens independently of the connection cap; a re-registration under an existing id is not new growth.</summary>
        public int MaxRegisteredScreens { get; set; } = 16;
    }

    private readonly IHubContext<ScreenHub> _hubContext;
    private readonly IScreenKeyStore _keyStore;
    private readonly ServiceOptions _options;
    private readonly ILogger<ScreenServerService>? _logger;
    private readonly Dictionary<string, ScreenConnection> _connections = [];
    private readonly HashSet<string> _liveConnectionIds = [];
    private readonly Dictionary<string, SessionAuth> _sessions = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

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
        _lock.Wait();
        try
        {
            if (_liveConnectionIds.Count >= _options.MaxConcurrentConnections) return false;
            return _liveConnectionIds.Add(connectionId);
        }
        finally { _lock.Release(); }
    }

    void IHubCallback.ReleaseConnectionSlot(string connectionId)
    {
        _lock.Wait();
        try { _liveConnectionIds.Remove(connectionId); }
        finally { _lock.Release(); }
    }

    string IHubCallback.BeginSession(string connectionId)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        _lock.Wait();
        try { _sessions[connectionId] = new SessionAuth { Nonce = nonce }; }
        finally { _lock.Release(); }

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

        _lock.Wait();
        try
        {
            if (!_sessions.TryGetValue(connectionId, out var session)) return false;

            if (!ScreenMessageAuth.Verify(key, session.Nonce, envelope.Seq, envelope.Payload, envelope.Mac)
                || envelope.Seq <= session.ExpectedInboundSeq)
                return false;

            var payload = RegisterPayload.TryParse(envelope.Payload);
            if (payload is null) return false;

            // A re-registration under an existing id overwrites in place, so it does not count against the cap.
            if (!_connections.ContainsKey(envelope.ScreenId) && _connections.Count >= _options.MaxRegisteredScreens)
                return false;

            session.Key = key;
            session.ScreenId = envelope.ScreenId;
            session.ExpectedInboundSeq = envelope.Seq;

            var conn = new ScreenConnection
            {
                ScreenId = envelope.ScreenId,
                ConnectionId = connectionId,
                ConnectedAt = DateTime.UtcNow,
                HostAddress = hostAddress,
                Capabilities = payload.ToCapabilities(),
            };
            _connections[envelope.ScreenId] = conn;
            ScreenConnected?.Invoke(this, new ScreenConnectionEventArgs { Connection = conn });
            return true;
        }
        finally { _lock.Release(); }
    }

    bool IHubCallback.TryAcceptState(string connectionId, string envelopeJson)
    {
        var envelope = SignedEnvelope.TryParse(envelopeJson);
        if (envelope is null) return false;

        IScreenState? state;

        _lock.Wait();
        try
        {
            if (!_sessions.TryGetValue(connectionId, out var session) || session.Key is null) return false;
            if (envelope.ScreenId != session.ScreenId) return false;

            if (!ScreenMessageAuth.Verify(session.Key, session.Nonce, envelope.Seq, envelope.Payload, envelope.Mac)
                || envelope.Seq <= session.ExpectedInboundSeq)
                return false;

            state = ScreenIpcSerializer.DeserializeState(envelope.Payload);
            if (state is null) return false;

            session.ExpectedInboundSeq = envelope.Seq;
        }
        finally { _lock.Release(); }

        // Raised outside the lock: a handler that enumerates connected screens must not deadlock on it.
        StateReceived?.Invoke(this, new ScreenStateReceivedEventArgs { ScreenId = envelope.ScreenId, State = state });
        return true;
    }

    bool IHubCallback.IsAuthenticated(string connectionId)
    {
        _lock.Wait();
        try { return _sessions.TryGetValue(connectionId, out var session) && session.Key is not null; }
        finally { _lock.Release(); }
    }

    void IHubCallback.OnScreenDisconnected(string connectionId)
    {
        _lock.Wait();
        try
        {
            _sessions.Remove(connectionId);

            var conn = _connections.Values.FirstOrDefault(c => c.ConnectionId == connectionId);
            if (conn is null) return;
            _connections.Remove(conn.ScreenId);
            ScreenDisconnected?.Invoke(this, new ScreenConnectionEventArgs { Connection = conn });
        }
        finally { _lock.Release(); }
    }

    public async IAsyncEnumerable<IScreenConnection> GetConnectedScreensAsync()
    {
        List<IScreenConnection> snapshot;
        await _lock.WaitAsync();
        try { snapshot = [.. _connections.Values]; }
        finally { _lock.Release(); }

        foreach (var conn in snapshot)
            yield return conn;
    }

    public async Task SendCommandAsync(string screenId, IScreenCommand command)
    {
        ScreenConnection? conn;
        await _lock.WaitAsync();
        try { _connections.TryGetValue(screenId, out conn); }
        finally { _lock.Release(); }

        if (conn is null) return;

        await SendToAsync(conn, command);
    }

    public async Task BroadcastCommandAsync(IScreenCommand command)
    {
        // Every command is signed with the addressed screen's own key, so there is no Clients.All
        // shortcut any more — a single message cannot carry a MAC every screen would accept. Each
        // gets its own, which is also where the per-screen stream URL was already handled.
        List<ScreenConnection> snapshot;
        await _lock.WaitAsync();
        try { snapshot = [.. _connections.Values]; }
        finally { _lock.Release(); }

        await Task.WhenAll(snapshot.Select(conn => SendToAsync(conn, command)));
    }

    private async Task SendToAsync(ScreenConnection connection, IScreenCommand command)
    {
        var payload = ScreenIpcSerializer.SerializeCommand(Reachable(command, connection));

        string nonce;
        byte[] key;
        long seq;

        await _lock.WaitAsync();
        try
        {
            // Only a screen that finished the handshake has a key; one still registering is skipped
            // rather than sent an unsigned command it would reject anyway.
            if (!_sessions.TryGetValue(connection.ConnectionId, out var session) || session.Key is null) return;

            nonce = session.Nonce;
            key = session.Key;
            seq = ++session.OutboundSeq;
        }
        finally { _lock.Release(); }

        var envelope = new SignedEnvelope(
            connection.ScreenId, seq, payload, ScreenMessageAuth.Sign(key, nonce, seq, payload));

        await _hubContext.Clients.Client(connection.ConnectionId).SendAsync("ReceiveCommand", envelope.ToJson());
    }

    private IScreenCommand Reachable(IScreenCommand command, ScreenConnection connection)
    {
        if (command is not LoadMediaCommand load) return command;

        var url = StreamUrlRewriter.ForScreen(load.StreamUrl, connection.HostAddress);
        if (ReferenceEquals(url, load.StreamUrl)) return command;

        // Worth a line: when a screen plays nothing, the first question is which address it was
        // told to fetch from.
        _logger?.LogInformation("Screen {ScreenId} will fetch the stream from {Url}", connection.ScreenId, url);

        return new LoadMediaCommand
        {
            StreamUrl = url,
            StreamStartOffset = load.StreamStartOffset,
        };
    }

    private sealed class SessionAuth
    {
        public required string Nonce { get; init; }
        public byte[]? Key { get; set; }
        public string? ScreenId { get; set; }
        public long ExpectedInboundSeq { get; set; }
        public long OutboundSeq { get; set; }
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
