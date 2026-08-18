using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sharpcaster;
using Sharpcaster.Models;
using Sharpcaster.Models.Media;

namespace KHost.Cast;

/// <summary>
/// Reaches Cast receivers, so a Chromecast is just another screen to everything above
/// <see cref="IScreenServer"/>. It is deliberately not sync-capable: a Cast device plays on its own
/// schedule with no way to be held to anyone else's, so it is always a loose consumer.
/// </summary>
public sealed class CastScreenTransport : IScreenTransport, ICastScreenService, IDisposable
{
    public sealed class ServiceOptions
    {
        public const string SectionName = "Cast";

        /// <summary>Off by default: discovery browses the whole network, which is not free.</summary>
        public bool Enabled { get; set; }

        /// <summary>Google's Default Media Receiver — plays a plain URL with no receiver app of our own.</summary>
        public string ReceiverAppId { get; set; } = "CC1AD845";

        /// <summary>How long a discovery sweep listens before reporting what it heard.</summary>
        public TimeSpan DiscoveryTimeout { get; set; } = TimeSpan.FromSeconds(5);
    }

    private readonly ServiceOptions _options;
    private readonly ILogger<CastScreenTransport> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private readonly Dictionary<string, ChromecastReceiver> _discovered = [];
    private readonly Dictionary<string, CastConnection> _attached = [];

    private ChromecastLocator? _locator;

    public event EventHandler<ScreenConnectionEventArgs>? ScreenConnected;
    public event EventHandler<ScreenConnectionEventArgs>? ScreenDisconnected;
    public event EventHandler<ScreenStateReceivedEventArgs>? StateReceived;
    public event EventHandler? StateChanged;

    public CastScreenTransport(ILogger<CastScreenTransport> logger, IOptions<ServiceOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public IReadOnlyList<CastDevice> Devices
    {
        get
        {
            _lock.Wait();
            try
            {
                return [.. _discovered.Select(entry => new CastDevice
                {
                    Id = entry.Key,
                    Name = entry.Value.Name ?? entry.Key,
                    Model = entry.Value.Model,
                    Address = entry.Value.DeviceUri?.Host,
                    IsAttached = _attached.ContainsKey(entry.Key),
                })];
            }
            finally { _lock.Release(); }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Cast is disabled; set Cast:Enabled to browse for receivers");
            return;
        }

        _locator = new ChromecastLocator();
        _locator.ChromecastReceiverFound += OnReceiverFound;

        // One sweep now so the screens page has something immediately, then keep listening.
        try
        {
            foreach (var receiver in await _locator.FindReceiversAsync(_options.DiscoveryTimeout))
                Remember(receiver);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cast discovery sweep failed");
        }

        _locator.StartContinuousDiscovery(TimeSpan.FromSeconds(30));
        RaiseStateChanged();
    }

    public async Task<bool> AttachAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        ChromecastReceiver? receiver;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_attached.ContainsKey(deviceId)) return true;
            if (!_discovered.TryGetValue(deviceId, out receiver)) return false;
        }
        finally { _lock.Release(); }

        var name = receiver!.Name ?? deviceId;
        _logger.LogInformation("Attaching Cast device {Name} at {Address}", name, receiver.DeviceUri);

        var client = new ChromecastClient();

        try
        {
            await client.ConnectChromecast(receiver);
            await client.LaunchApplicationAsync(_options.ReceiverAppId, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not attach Cast device {Name}", name);
            try { await client.DisconnectAsync(); } catch { /* already gone */ }
            return false;
        }

        var connection = new CastConnection(deviceId, name, client);

        await _lock.WaitAsync(cancellationToken);
        try { _attached[deviceId] = connection; }
        finally { _lock.Release(); }

        client.MediaChannel.StatusChanged += (_, status) => OnMediaStatus(connection, status);
        client.Disconnected += (_, _) => OnDeviceDropped(deviceId);

        ScreenConnected?.Invoke(this, new ScreenConnectionEventArgs { Connection = connection });
        RaiseStateChanged();

        return true;
    }

    public async Task DetachAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        CastConnection? connection;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!_attached.Remove(deviceId, out connection)) return;
        }
        finally { _lock.Release(); }

        _logger.LogInformation("Detaching Cast device {Name}", connection!.ScreenId);

        try
        {
            await connection.Client.ReceiverChannel.StopApplication();
            await connection.Client.DisconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Untidy detach of {Name}", connection.ScreenId);
        }

        ScreenDisconnected?.Invoke(this, new ScreenConnectionEventArgs { Connection = connection });
        RaiseStateChanged();
    }

    public async IAsyncEnumerable<IScreenConnection> GetConnectedScreensAsync()
    {
        List<CastConnection> snapshot;

        await _lock.WaitAsync();
        try { snapshot = [.. _attached.Values]; }
        finally { _lock.Release(); }

        foreach (var connection in snapshot)
            yield return connection;
    }

    public async Task<bool> SendCommandAsync(string screenId, IScreenCommand command)
    {
        CastConnection? connection;

        await _lock.WaitAsync();
        try
        {
            connection = _attached.Values.FirstOrDefault(c => c.ScreenId == screenId);
        }
        finally { _lock.Release(); }

        if (connection is null) return false;

        var media = connection.Client.MediaChannel;

        switch (command)
        {
            case LoadMediaCommand cmd:
                await LoadAsync(connection, cmd);
                break;

            case PlayCommand:
                await media.PlayAsync();
                break;

            case PauseCommand:
                await media.PauseAsync();
                break;

            case StopCommand:
                // No fade: the Cast protocol has no ramp, and faking one by stepping the receiver
                // volume would move the TV's own level rather than just ours.
                await media.StopAsync();
                break;

            case SeekCommand cmd:
                await media.SeekAsync((cmd.Position - connection.StreamStartOffset).TotalSeconds);
                break;

            case SetVolumeCommand cmd:
                await MuteAsync(connection, cmd.Volume <= 0f);
                break;

            case SetTimelineCommand:
                // Never sent to a Cast device — it declares itself unsyncable — but a broadcast
                // could still reach here, and honouring it would be a lie.
                break;

            default:
                _logger.LogDebug("Cast device {Name} cannot handle {Command}",
                    connection.ScreenId, command.GetType().Name);
                break;
        }

        return true;
    }

    private async Task LoadAsync(CastConnection connection, LoadMediaCommand command)
    {
        if (command.StreamUrl is not { Length: > 0 } url)
        {
            // A Cast device has no access to the host's filesystem, so a path is useless to it.
            _logger.LogError("Cannot cast '{FilePath}' to {Name}: no host stream URL",
                command.FilePath, connection.ScreenId);
            return;
        }

        var reachable = MakeReachableFromDevice(url, LanAddress());
        connection.StreamStartOffset = command.StreamStartOffset;

        _logger.LogInformation("Casting {Url} to {Name}", reachable, connection.ScreenId);

        await connection.Client.MediaChannel.LoadAsync(
            new Media { ContentUrl = reachable, StreamType = StreamType.Buffered }, autoPlay: false);
    }

    /// <summary>
    /// Stream volume would leave the TV's own level alone, but Sharpcaster cannot address it
    /// reliably, so muting is device-wide. A receiver with fixed volume refuses outright — that is
    /// the device declining, not a bug, and the screen simply stays audible.
    /// </summary>
    private async Task MuteAsync(CastConnection connection, bool muted)
    {
        try
        {
            await connection.Client.ReceiverChannel.SetMute(muted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cast device {Name} refused mute={Muted}", connection.ScreenId, muted);
        }
    }

    /// <summary>
    /// The host resolves its own base address to localhost, which means nothing on the far side of
    /// the network — a Cast device fetching it would be fetching itself. Swap in a LAN address.
    /// Anything already non-loopback is left alone, so a configured address still wins.
    /// </summary>
    internal static string MakeReachableFromDevice(string url, string? lanAddress)
    {
        if (lanAddress is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        if (!uri.IsLoopback) return url;

        return new UriBuilder(uri) { Host = lanAddress }.Uri.ToString();
    }

    private static string? LanAddress()
        => NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
            ?.ToString();

    private void OnMediaStatus(CastConnection connection, MediaStatus? status)
    {
        if (status is null) return;

        var playing = status.PlayerState == PlayerStateType.Playing;

        StateReceived?.Invoke(this, new ScreenStateReceivedEventArgs
        {
            ScreenId = connection.ScreenId,
            State = new ScreenPlaybackState
            {
                LoadedFilePath = status.Media?.ContentUrl,
                IsPlaying = playing,
                Position = connection.StreamStartOffset + TimeSpan.FromSeconds(status.CurrentTime),
                Duration = TimeSpan.FromSeconds(status.Media?.Duration ?? 0),

                // Deliberately null: a Cast device is never the primary, and offering a
                // sample time would invite the host to anchor the group on a report it cannot trust.
                SampledAtUtc = null,
            },
        });
    }

    private void OnDeviceDropped(string deviceId)
    {
        CastConnection? connection;

        _lock.Wait();
        try
        {
            if (!_attached.Remove(deviceId, out connection)) return;
        }
        finally { _lock.Release(); }

        _logger.LogWarning("Cast device {Name} dropped its connection", connection!.ScreenId);

        ScreenDisconnected?.Invoke(this, new ScreenConnectionEventArgs { Connection = connection });
        RaiseStateChanged();
    }

    private void OnReceiverFound(object? sender, ChromecastReceiverEventArgs e)
    {
        if (Remember(e.Receiver)) RaiseStateChanged();
    }

    private bool Remember(ChromecastReceiver receiver)
    {
        var id = IdOf(receiver);
        if (id is null) return false;

        _lock.Wait();
        try
        {
            if (!_discovered.TryAdd(id, receiver)) return false;
        }
        finally { _lock.Release(); }

        _logger.LogInformation("Found Cast device {Name} at {Address}", receiver.Name, receiver.DeviceUri);
        return true;
    }

    // Discovery does not always carry a stable device id, and the friendly name is what the user
    // recognises anyway — it is also what the screen is called once attached.
    private static string? IdOf(ChromecastReceiver receiver) => receiver.Name;

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_locator is not null)
        {
            _locator.ChromecastReceiverFound -= OnReceiverFound;
            try { _locator.StopContinuousDiscovery(); } catch { /* never started */ }
            _locator.Dispose();
        }

        foreach (var connection in _attached.Values)
        {
            try { connection.Client.DisconnectAsync().GetAwaiter().GetResult(); } catch { /* shutting down */ }
        }

        _attached.Clear();
    }

    private sealed class CastConnection(string deviceId, string screenId, ChromecastClient client) : IScreenConnection
    {
        public ChromecastClient Client { get; } = client;

        /// <summary>Song position the current stream's zero maps to, from the last load.</summary>
        public TimeSpan StreamStartOffset { get; set; }

        public string ScreenId { get; } = screenId;
        public string? ConnectionId { get; } = deviceId;
        public DateTime ConnectedAt { get; } = DateTime.UtcNow;
        public bool IsConnected => true;

        public ScreenCapabilities Capabilities => ScreenCapabilities.CastDevice;
    }
}
