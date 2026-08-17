namespace KHost.Abstractions.Services;

/// <summary>
/// Discovers Cast receivers and attaches the ones the user picks. Discovery is not attachment on
/// purpose: every Chromecast on the network shows up, and taking one over uninvited would hijack
/// whatever the household is already watching.
/// </summary>
public interface ICastScreenService
{
    event EventHandler? StateChanged;

    /// <summary>Begins browsing for receivers. Safe to call when casting is disabled — it no-ops.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Receivers seen on the network, attached or not.</summary>
    IReadOnlyList<CastDevice> Devices { get; }

    /// <summary>
    /// Connects, launches the media receiver, and publishes the device as a screen. Returns false
    /// if it could not be reached.
    /// </summary>
    Task<bool> AttachAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Stops the receiver app and drops the device as a screen.</summary>
    Task DetachAsync(string deviceId, CancellationToken cancellationToken = default);
}

/// <summary>A Cast receiver on the network.</summary>
public sealed class CastDevice
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Model { get; init; }
    public string? Address { get; init; }

    /// <summary>True once connected and published as a screen.</summary>
    public bool IsAttached { get; init; }
}
