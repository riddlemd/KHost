namespace KHost.Abstractions.Services;

/// <summary>
/// Casting to a receiver. Deliberately not a screen: it cannot hold the group timeline, we
/// connect to it rather than it to us, and only one is driven at a time.
/// </summary>
public interface ICastService
{
    event EventHandler? StateChanged;

    /// <summary>
    /// With no syncable screen present this is the only clock there is. A receiver buffers
    /// seconds, so a free-running timer ends the song while the room is still hearing it.
    /// </summary>
    event EventHandler<CastPlaybackStatus>? PlaybackStatusChanged;

    /// <summary>Begins browsing for receivers. Safe to call when casting is disabled — it no-ops.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<CastDevice> Devices { get; }

    string? ConnectedDeviceId { get; }

    /// <summary>Replaces whatever was connected before: one song, one receiver.</summary>
    Task<bool> ConnectAsync(string deviceId, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary><paramref name="startOffset"/> is the song position the stream's zero maps to.</summary>
    Task LoadAsync(string streamUrl, TimeSpan startOffset, CancellationToken cancellationToken = default);

    Task PlayAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);
}

public sealed class CastPlaybackStatus
{
    /// <summary>Absolute — the stream offset is already added.</summary>
    public required TimeSpan Position { get; init; }

    public required bool IsPlaying { get; init; }

    /// <summary>
    /// Arrival time, not sample time — there is no clock handshake with a receiver. Wrong by a
    /// LAN hop, which is nothing against the seconds of buffering this corrects for.
    /// </summary>
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
