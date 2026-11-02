using System.Net;
using System.Net.Sockets;
using System.Text;
using KHost.IPC.SignalR;

namespace KHost.UnitTests.IPC.SignalR;

/// <summary>A host that takes the socket and then goes quiet (shutting down mid-request, or gone off the
/// network) must not hold a screen's connect, and so its reconnect, forever.</summary>
public sealed class ScreenClientConnectTimeoutTests : IAsyncDisposable
{
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromMilliseconds(500);

    // Well past the attempt timeout, well short of anything that would pass without one.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly SilentHost _host = new();

    [Fact]
    public async Task Given_AHostThatNeverAnswersNegotiate_When_ConnectAsync_Then_ItFailsInsteadOfHanging()
    {
        // Given
        _host.AnswerNegotiate = false;
        // Disposal waits on a connect that never ends, so it is bounded or a regression hangs the run.
        await using var bounded = new BoundedDispose(new ScreenClient(null) { ConnectAttemptTimeout = AttemptTimeout });
        var client = bounded.Client;

        // When
        // Frees a connect that never gives up on its own, so a regression fails here instead of hanging the run.
        using var giveUp = new CancellationTokenSource(Patience + TimeSpan.FromSeconds(5));
        var connect = client.ConnectAsync(_host.Url, "Screen 1", authKey: new byte[32], cancellationToken: giveUp.Token);
        var finished = await Task.WhenAny(connect, Task.Delay(Patience));

        // Then
        Assert.Same(connect, finished);
        await Assert.ThrowsAnyAsync<Exception>(() => connect);
        Assert.Equal(1, _host.NegotiatesSeen);
    }

    [Fact]
    public async Task Given_AHostThatNeverAnswersTheWebSocketUpgrade_When_ConnectAsync_Then_ItFailsInsteadOfHanging()
    {
        // Given
        _host.AnswerNegotiate = true;
        // Disposal waits on a connect that never ends, so it is bounded or a regression hangs the run.
        await using var bounded = new BoundedDispose(new ScreenClient(null) { ConnectAttemptTimeout = AttemptTimeout });
        var client = bounded.Client;

        // When
        // Frees a connect that never gives up on its own, so a regression fails here instead of hanging the run.
        using var giveUp = new CancellationTokenSource(Patience + TimeSpan.FromSeconds(5));
        var connect = client.ConnectAsync(_host.Url, "Screen 1", authKey: new byte[32], cancellationToken: giveUp.Token);
        var finished = await Task.WhenAny(connect, Task.Delay(Patience));

        // Then
        Assert.Same(connect, finished);
        await Assert.ThrowsAnyAsync<Exception>(() => connect);
        Assert.True(_host.UpgradesSeen >= 1, "the client never reached the WebSocket upgrade");
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private sealed class BoundedDispose(ScreenClient client) : IAsyncDisposable
    {
        public ScreenClient Client => client;

        public async ValueTask DisposeAsync()
        {
            try { await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { }
        }
    }

    /// <summary>Accepts every socket and reads the request; answers negotiate only when told to, and never
    /// answers anything else.</summary>
    private sealed class SilentHost : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly List<TcpClient> _held = [];
        private readonly Task _accepting;
        private int _negotiates;
        private int _upgrades;

        public SilentHost()
        {
            _listener.Start();
            _accepting = AcceptAsync();
        }

        public volatile bool AnswerNegotiate;

        public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/ipc/screen";

        public int NegotiatesSeen => Volatile.Read(ref _negotiates);

        public int UpgradesSeen => Volatile.Read(ref _upgrades);

        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    var socket = await _listener.AcceptTcpClientAsync(_stop.Token);
                    lock (_held) _held.Add(socket);
                    _ = ServeAsync(socket);
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
        }

        private async Task ServeAsync(TcpClient socket)
        {
            try
            {
                var stream = socket.GetStream();
                var head = await ReadHeadAsync(stream);
                if (head.StartsWith("POST ", StringComparison.Ordinal) && head.Contains("/negotiate", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref _negotiates);
                    if (!AnswerNegotiate) return;

                    const string body = """{"negotiateVersion":1,"connectionId":"c1","connectionToken":"t1","availableTransports":[{"transport":"WebSockets","transferFormats":["Text","Binary"]}]}""";
                    var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(response), _stop.Token);
                    // Keep-alive: the upgrade may come on this same socket.
                    head = await ReadHeadAsync(stream);
                }

                if (head.StartsWith("GET ", StringComparison.Ordinal))
                    Interlocked.Increment(ref _upgrades);
            }
            catch (Exception) when (_stop.IsCancellationRequested) { }
            catch (IOException) { }
        }

        private async Task<string> ReadHeadAsync(NetworkStream stream)
        {
            var head = new StringBuilder();
            var buffer = new byte[1];
            while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (await stream.ReadAsync(buffer, _stop.Token) == 0) break;
                head.Append((char)buffer[0]);
            }
            return head.ToString();
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            lock (_held) foreach (var socket in _held) socket.Dispose();
            await _accepting;
        }
    }
}
