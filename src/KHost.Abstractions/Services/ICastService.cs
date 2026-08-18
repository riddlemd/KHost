namespace KHost.Abstractions.Services;

/// <summary>
/// Casting to a receiver on the network. Deliberately not a screen: a Cast device cannot be held
/// to the group timeline, so it can never be the primary, and every capability the screens grow
/// from here would be a lie on it. It also inverts the direction — screens dial in to us, whereas
/// a receiver sits there until we connect to it — and only one can be driven at a time.
/// </summary>
public interface ICastService
{
    event EventHandler? StateChanged;

    /// <summary>
    /// Where the receiver says it actually is. With no syncable screen present this is the only
    /// clock there is, and the host has to follow it — a receiver buffers seconds, so a free-running
    /// timer would reach the end of the song while the room is still hearing it.
    /// </summary>
    event EventHandler<CastPlaybackStatus>? PlaybackStatusChanged;

    /// <summary>Begins browsing for receivers. Safe to call when casting is disabled — it no-ops.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Receivers seen on the network, connected or not.</summary>
    IReadOnlyList<CastDevice> Devices { get; }

    /// <summary>The one receiver currently being driven, or null.</summary>
    string? ConnectedDeviceId { get; }

    /// <summary>
    /// Connects and launches the media receiver, replacing whatever was connected before — the
    /// host drives one at a time, because it has one song to play.
    /// </summary>
    Task<bool> ConnectAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Stops the receiver app and lets go of the device.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Points the receiver at a host stream. <paramref name="startOffset"/> is the song position
    /// the stream's own zero maps to, so reported positions can be made absolute.
    /// </summary>
    Task LoadAsync(string streamUrl, TimeSpan startOffset, CancellationToken cancellationToken = default);

    Task PlayAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);
}

/// <summary>A position report from the receiver.</summary>
public sealed class CastPlaybackStatus
{
    /// <summary>Absolute song position — the stream offset is already added.</summary>
    public required TimeSpan Position { get; init; }

    public required bool IsPlaying { get; init; }

    /// <summary>
    /// When the report reached us. There is no clock handshake with a receiver, so this is the
    /// arrival time rather than the sample time — wrong by a LAN hop, which is nothing against
    /// the seconds of buffering it exists to correct for.
    /// </summary>
    public required DateTime SampledAtUtc { get; init; }
}

/// <summary>A Cast receiver on the network.</summary>
public sealed class CastDevice
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Model { get; init; }
    public string? Address { get; init; }

    /// <summary>True for the one device currently being driven.</summary>
    public bool IsConnected { get; init; }
}
