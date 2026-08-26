namespace KHost.Abstractions.Models.Plugins;

public enum PluginInstallState
{
    Downloading,
    /// <summary>Hashing the download and reading the manifest out of it.</summary>
    Verifying,
    /// <summary>Payload is staged and applies on the next start.</summary>
    Staged,
    Failed,
    Cancelled,
}

/// <summary>One install this process has run, active or settled, for the Plugins page.</summary>
public sealed record PluginInstallInfo
{
    public required Guid PluginId { get; init; }

    public required string Name { get; init; }

    public required string Version { get; init; }

    public required DateTime StartedUtc { get; init; }

    public PluginInstallState State { get; init; } = PluginInstallState.Downloading;

    /// <summary>0..1, or null when the server sent no length — rendered as an indeterminate bar.</summary>
    public double? Progress { get; init; }

    /// <summary>Why it failed. Null in every other state.</summary>
    public string? Error { get; init; }
}
