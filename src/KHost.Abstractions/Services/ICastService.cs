namespace KHost.Abstractions.Services;

/// <summary>Casting to a receiver, not a screen: no group timeline, one connection at a time.</summary>
public interface ICastService
{

    /// <summary>The only clock with no syncable screen; a free timer would end the song early.</summary>
    event EventHandler<CastPlaybackStatus>? PlaybackStatusChanged;

    /// <summary>Browsing sweeps the whole network, so it is off until someone asks for it.</summary>
    bool IsDiscovering { get; }

    Task StartDiscoveryAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops browsing and forgets what it found; an active cast is left alone.</summary>
    Task StopDiscoveryAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<CastDevice> Devices { get; }

    string? ConnectedDeviceId { get; }

    /// <summary>Identifies the receiver session; changes on reconnect. Null if disconnected.</summary>
    Guid? SessionId { get; }

    /// <summary>Replaces whatever was connected before: one song, one receiver.</summary>
    Task<bool> ConnectAsync(string deviceId, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary><paramref name="tempo"/> converts the receiver's seconds back to song seconds.</summary>
    Task LoadAsync(string streamUrl, TimeSpan startOffset, int tempo = 0, CancellationToken cancellationToken = default);

    Task PlayAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);
}

public sealed class CastPlaybackStatus
{
    /// <summary>Absolute: the stream offset is already added.</summary>
    public required TimeSpan Position { get; init; }

    public required bool IsPlaying { get; init; }

    /// <summary>Arrival time, not sample time: no clock handshake; off by a LAN hop, negligible.</summary>
    public required DateTime SampledAtUtc { get; init; }
}

public sealed class CastDevice
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Model { get; init; }
    public string? Address { get; init; }

    public bool IsConnected { get; init; }
}
