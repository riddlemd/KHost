using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using KHost.IPC.SignalR;
using KHost.IPC.SignalR.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KHost.UnitTests.IPC.SignalR;

/// <summary>A real screen hub on a fixed loopback port that can go down and come back, so a client's
/// retry loops meet a host that is genuinely unreachable and then genuinely answers again.</summary>
internal sealed class LoopbackScreenHost : IAsyncDisposable
{
    private readonly Dictionary<string, byte[]> _keys = [];
    private WebApplication? _app;

    public LoopbackScreenHost()
    {
        // Port 0 would give a different port on each start; a client retries the one URL it was given.
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        Port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
    }

    public int Port { get; }

    public string Url => $"http://127.0.0.1:{Port}/ipc/screen";

    public ScreenServerService Server => _app?.Services.GetRequiredService<ScreenServerService>()
        ?? throw new InvalidOperationException("The host is not running.");

    public byte[] Provision(string screenId) => _keys[screenId] = RandomNumberGenerator.GetBytes(32);

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{Port}");
        builder.Logging.ClearProviders();
        // A stop with a client still attached otherwise sits out the 30 s graceful drain.
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.Zero);
        builder.Services.AddSignalR().AddJsonProtocol(o =>
            o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, ScreenCommandJsonContext.Default));
        builder.Services.AddOptions<ScreenServerService.ServiceOptions>();
        builder.Services.AddSingleton<IScreenKeyStore>(new KeyStore(_keys));
        builder.Services.AddSingleton<ScreenServerService>();
        builder.Services.AddSingleton<IScreenServer>(sp => sp.GetRequiredService<ScreenServerService>());
        builder.Services.AddSingleton<IHubCallback>(sp => sp.GetRequiredService<ScreenServerService>());

        _app = builder.Build();
        _app.MapHub<ScreenHub>("/ipc/screen");
        await _app.StartAsync();
    }

    public async Task StopAsync()
    {
        if (_app is null) return;

        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
    }

    public async Task<string?> RegisteredScreenIdAsync()
    {
        await foreach (var screen in Server.GetConnectedScreensAsync())
            return screen.ScreenId;

        return null;
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private sealed class KeyStore(Dictionary<string, byte[]> keys) : IScreenKeyStore
    {
        public string Provision(string screenId)
        {
            keys[screenId] = RandomNumberGenerator.GetBytes(32);
            return $"/keys/{screenId}.key";
        }

        public byte[]? GetKey(string screenId) => keys.GetValueOrDefault(screenId);

        public void Revoke(string screenId) => keys.Remove(screenId);
    }
}

/// <summary>Every line logged through it, from any category.</summary>
internal sealed class RecordingLog : ILoggerFactory, ILogger
{
    private readonly Lock _lock = new();
    private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries
    {
        get { lock (_lock) { return [.. _entries]; } }
    }

    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Containing(string text)
        => [.. Entries.Where(e => e.Message.Contains(text, StringComparison.Ordinal))];

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_lock) { _entries.Add((logLevel, formatter(state, exception), exception)); }
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public ILogger CreateLogger(string categoryName) => this;

    public void AddProvider(ILoggerProvider provider) { }

    public void Dispose() { }
}
