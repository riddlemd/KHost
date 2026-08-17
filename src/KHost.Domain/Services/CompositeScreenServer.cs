using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

/// <summary>
/// The one <see cref="IScreenServer"/> the rest of the app talks to, fanned out over every
/// transport. A screen reached over SignalR and a Cast device reached over CASTV2 are both just
/// screens from here up, which is what lets a Cast device hold a role and be muted like any other.
/// </summary>
public sealed class CompositeScreenServer : IScreenServer, IDisposable
{
    private readonly IReadOnlyList<IScreenTransport> _transports;
    private readonly ILogger<CompositeScreenServer> _logger;

    public event EventHandler<ScreenConnectionEventArgs>? ScreenConnected;
    public event EventHandler<ScreenConnectionEventArgs>? ScreenDisconnected;
    public event EventHandler<ScreenStateReceivedEventArgs>? StateReceived;

    public CompositeScreenServer(IEnumerable<IScreenTransport> transports, ILogger<CompositeScreenServer> logger)
    {
        _transports = [.. transports];
        _logger = logger;

        foreach (var transport in _transports)
        {
            transport.ScreenConnected += OnScreenConnected;
            transport.ScreenDisconnected += OnScreenDisconnected;
            transport.StateReceived += OnStateReceived;
        }
    }

    public async IAsyncEnumerable<IScreenConnection> GetConnectedScreensAsync()
    {
        foreach (var transport in _transports)
            await foreach (var screen in transport.GetConnectedScreensAsync())
                yield return screen;
    }

    public async Task SendCommandAsync(string screenId, IScreenCommand command)
    {
        foreach (var transport in _transports)
        {
            try
            {
                if (await transport.SendCommandAsync(screenId, command)) return;
            }
            catch (Exception ex)
            {
                // One transport failing must not stop the others being tried, or a sick Cast
                // connection would black-hole commands meant for a perfectly healthy screen.
                _logger.LogWarning(ex, "Transport {Transport} failed sending {Command} to {ScreenId}",
                    transport.GetType().Name, command.GetType().Name, screenId);
            }
        }

        _logger.LogWarning("No transport owns screen {ScreenId}; dropped {Command}",
            screenId, command.GetType().Name);
    }

    /// <summary>
    /// Addressed per screen rather than by a transport-level broadcast: only the transport knows
    /// its own connections, and SignalR's own "all clients" would miss every Cast device.
    /// </summary>
    public async Task BroadcastCommandAsync(IScreenCommand command)
    {
        foreach (var transport in _transports)
        {
            List<string> screenIds = [];

            try
            {
                await foreach (var screen in transport.GetConnectedScreensAsync())
                    screenIds.Add(screen.ScreenId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not enumerate {Transport}", transport.GetType().Name);
                continue;
            }

            foreach (var screenId in screenIds)
            {
                try
                {
                    await transport.SendCommandAsync(screenId, command);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed sending {Command} to {ScreenId}",
                        command.GetType().Name, screenId);
                }
            }
        }
    }

    private void OnScreenConnected(object? sender, ScreenConnectionEventArgs e) => ScreenConnected?.Invoke(this, e);

    private void OnScreenDisconnected(object? sender, ScreenConnectionEventArgs e) => ScreenDisconnected?.Invoke(this, e);

    private void OnStateReceived(object? sender, ScreenStateReceivedEventArgs e) => StateReceived?.Invoke(this, e);

    public void Dispose()
    {
        foreach (var transport in _transports)
        {
            transport.ScreenConnected -= OnScreenConnected;
            transport.ScreenDisconnected -= OnScreenDisconnected;
            transport.StateReceived -= OnStateReceived;
        }
    }
}
