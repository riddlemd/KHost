using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sharpcaster;
using Sharpcaster.Models;
using Sharpcaster.Models.Media;

namespace KHost.Cast;

/// <summary>Drives one Cast receiver at a time.</summary>
public sealed class CastService : ICastService, IDisposable
{
    public sealed class ServiceOptions
    {
        /// <summary>Google's Default Media Receiver — plays a plain URL, no app of our own.</summary>
        public string ReceiverAppId { get; set; } = "CC1AD845";

        public TimeSpan DiscoveryTimeout { get; set; } = TimeSpan.FromSeconds(5);
    }

    private readonly ServiceOptions _options;
    private readonly ILogger<CastService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, ChromecastReceiver> _discovered = [];
    private readonly IMessageBroker? _broker;

    private ChromecastLocator? _locator;
    private ChromecastClient? _client;
    private string? _connectedDeviceId;
    private TimeSpan _streamStartOffset;

    public event EventHandler<CastPlaybackStatus>? PlaybackStatusChanged;

    public CastService(ILogger<CastService> logger, IOptions<ServiceOptions> options, IMessageBroker? broker = null)
    {
        _logger = logger;
        _options = options.Value;
        _broker = broker;
    }

    public string? ConnectedDeviceId => _connectedDeviceId;

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
                    IsConnected = entry.Key == _connectedDeviceId,
                })];
            }
            finally { _lock.Release(); }
        }
    }

    public bool IsDiscovering => _locator is not null;

    public async Task StartDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        if (_locator is not null) return;

        var locator = new ChromecastLocator();
        locator.ChromecastReceiverFound += OnReceiverFound;
        _locator = locator;

        _logger.LogInformation("Browsing for Cast receivers");

        // One sweep now so the page has something immediately, then keep listening.
        try
        {
            foreach (var receiver in await locator.FindReceiversAsync(_options.DiscoveryTimeout))
                Remember(receiver);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cast discovery sweep failed");
        }

        locator.StartContinuousDiscovery(TimeSpan.FromSeconds(30));
        RaiseStateChanged();
    }

    public Task StopDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        var locator = _locator;
        _locator = null;

        if (locator is not null)
        {
            locator.ChromecastReceiverFound -= OnReceiverFound;
            try { locator.StopContinuousDiscovery(); } catch { /* never started */ }
            _logger.LogInformation("Stopped browsing for Cast receivers");
        }

        _lock.Wait(cancellationToken);
        try
        {
            // The receiver being cast to stays listed, or there would be no way to stop it.
            foreach (var id in _discovered.Keys.Where(id => id != _connectedDeviceId).ToList())
                _discovered.Remove(id);
        }
        finally { _lock.Release(); }

        RaiseStateChanged();
        return Task.CompletedTask;
    }

    public async Task<bool> ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (_connectedDeviceId == deviceId) return true;

        // One song, one receiver — a second would be a room hearing it seconds out of step.
        if (_connectedDeviceId is not null) await DisconnectAsync(cancellationToken);

        ChromecastReceiver? receiver;
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!_discovered.TryGetValue(deviceId, out receiver)) return false;
        }
        finally { _lock.Release(); }

        var name = receiver!.Name ?? deviceId;
        _logger.LogInformation("Connecting to Cast device {Name} at {Address}", name, receiver.DeviceUri);

        var client = new ChromecastClient();

        try
        {
            await client.ConnectChromecast(receiver);
            await client.LaunchApplicationAsync(_options.ReceiverAppId, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not connect to Cast device {Name}", name);
            try { await client.DisconnectAsync(); } catch { /* already gone */ }
            return false;
        }

        client.MediaChannel.StatusChanged += (_, status) => OnMediaStatus(status);
        client.Disconnected += (_, _) => OnDeviceDropped(deviceId);

        _client = client;
        _connectedDeviceId = deviceId;
        _streamStartOffset = TimeSpan.Zero;

        RaiseStateChanged();
        return true;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var client = _client;
        var name = _connectedDeviceId;

        _client = null;
        _connectedDeviceId = null;

        if (client is null) return;

        _logger.LogInformation("Disconnecting from Cast device {Name}", name);

        try
        {
            await client.ReceiverChannel.StopApplication();
            await client.DisconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Untidy disconnect from {Name}", name);
        }

        RaiseStateChanged();
    }

    public async Task LoadAsync(string streamUrl, TimeSpan startOffset, CancellationToken cancellationToken = default)
    {
        if (_client is not { } client) return;

        var reachable = MakeReachableFromDevice(streamUrl, LanAddress());
        _streamStartOffset = startOffset;

        _logger.LogInformation("Casting {Url} to {Name}", reachable, _connectedDeviceId);

        await GuardAsync(() => client.MediaChannel.LoadAsync(
            new Media { ContentUrl = reachable, StreamType = StreamType.Buffered }, autoPlay: false));
    }

    public Task PlayAsync(CancellationToken cancellationToken = default)
        => _client is { } c ? GuardAsync(c.MediaChannel.PlayAsync) : Task.CompletedTask;

    public Task PauseAsync(CancellationToken cancellationToken = default)
        => _client is { } c ? GuardAsync(c.MediaChannel.PauseAsync) : Task.CompletedTask;

    // No fade: Cast has no volume ramp, and faking one would move the TV's own level.
    public Task StopAsync(CancellationToken cancellationToken = default)
        => _client is { } c ? GuardAsync(c.MediaChannel.StopAsync) : Task.CompletedTask;

    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
        => _client is { } c
            ? GuardAsync(() => c.MediaChannel.SeekAsync((position - _streamStartOffset).TotalSeconds))
            : Task.CompletedTask;

    /// <summary>A television switched off mid-song must not fail the performance.</summary>
    private async Task GuardAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Cast device {Name} refused a command", _connectedDeviceId); }
    }

    /// <summary>
    /// The host resolves its base address to localhost, which on a television means the
    /// television. Anything already routable is left alone so a configured address wins.
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

    private void OnMediaStatus(MediaStatus? status)
    {
        if (status is null) return;

        PlaybackStatusChanged?.Invoke(this, new CastPlaybackStatus
        {
            Position = _streamStartOffset + TimeSpan.FromSeconds(status.CurrentTime),
            IsPlaying = status.PlayerState == PlayerStateType.Playing,
            SampledAtUtc = DateTime.UtcNow,
        });
    }

    private void OnDeviceDropped(string deviceId)
    {
        if (_connectedDeviceId != deviceId) return;

        _logger.LogWarning("Cast device {Name} dropped its connection", deviceId);

        _client = null;
        _connectedDeviceId = null;
        RaiseStateChanged();
    }

    private void OnReceiverFound(object? sender, ChromecastReceiverEventArgs e)
    {
        if (Remember(e.Receiver)) RaiseStateChanged();
    }

    private bool Remember(ChromecastReceiver receiver)
    {
        // Discovery has no stable device id, and the friendly name is what the user recognises.
        if (receiver.Name is not { Length: > 0 } id) return false;

        _lock.Wait();
        try
        {
            if (!_discovered.TryAdd(id, receiver)) return false;
        }
        finally { _lock.Release(); }

        _logger.LogInformation("Found Cast device {Name} at {Address}", receiver.Name, receiver.DeviceUri);
        return true;
    }

    private void RaiseStateChanged()
    {
        if (_broker is { } broker)
            _ = broker.PublishAsync(new CastChanged());
    }

    public void Dispose()
    {
        if (_locator is not null)
        {
            _locator.ChromecastReceiverFound -= OnReceiverFound;
            try { _locator.StopContinuousDiscovery(); } catch { /* never started */ }
            _locator.Dispose();
        }

        try { _client?.DisconnectAsync().GetAwaiter().GetResult(); } catch { /* shutting down */ }
        _client = null;
    }
}
