namespace KHost.Abstractions.Models.Plugins;

/// <summary>Where one plugin install is up to, for the Plugins page's progress display.</summary>
public enum PluginInstallState
{
    /// <summary>The release zip is being fetched.</summary>
    Downloading,
    /// <summary>The download finished; its checksum and manifest are being checked before it is
    /// accepted.</summary>
    Verifying,
    /// <summary>Payload is staged and applies on the next start.</summary>
    Staged,
    /// <summary>The install did not complete; see <see cref="PluginInstallInfo.Error"/>.</summary>
    Failed,
    /// <summary>The host cancelled the install before it finished.</summary>
    Cancelled,
}

/// <summary>One install this process has run, active or settled, for the Plugins page.</summary>
public sealed record PluginInstallInfo
{
    /// <summary>The plugin being installed, matching its manifest and catalog ids.</summary>
    public required Guid PluginId { get; init; }

    /// <summary>The plugin's display name, for the progress row.</summary>
    public required string Name { get; init; }

    /// <summary>The release version being installed.</summary>
    public required string Version { get; init; }

    /// <summary>When this install began, in UTC.</summary>
    public required DateTime StartedUtc { get; init; }

    /// <summary>Where the install is up to right now.</summary>
    public PluginInstallState State { get; init; } = PluginInstallState.Downloading;

    /// <summary>Fraction complete, 0.0–1.0, or null when the total size is unknown and progress
    /// cannot be measured.</summary>
    public double? Progress { get; init; }

    /// <summary>Why it failed. Null in every other state.</summary>
    public string? Error { get; init; }
}
