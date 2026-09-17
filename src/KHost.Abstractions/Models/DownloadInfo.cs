namespace KHost.Abstractions.Models;

public enum DownloadState { Downloading, Completed, Failed, Cancelled }

/// <summary>Which half is running; the entry stays Downloading across both phases.</summary>
public enum DownloadPhase { Fetching, Processing }

/// <summary>A snapshot of one download, active or settled, for the Downloads page.</summary>
public sealed record DownloadInfo
{
    public required Guid MediaId { get; init; }
    public required string Title { get; init; }
    public string Artist { get; init; } = string.Empty;

    /// <summary>The provider's display name. Empty when the caller did not supply one.</summary>
    public string Source { get; init; } = string.Empty;

    public required DateTime StartedUtc { get; init; }

    /// <summary>When it reached a terminal state; null while it is still running.</summary>
    public DateTime? SettledUtc { get; init; }

    public DownloadState State { get; init; } = DownloadState.Downloading;

    /// <summary>Fetching until a provider says otherwise, since most never have a second phase.</summary>
    public DownloadPhase Phase { get; init; } = DownloadPhase.Fetching;

    /// <summary>0..1, or null while unreported, rendered as an indeterminate progress bar.</summary>
    public double? Progress { get; init; }

    /// <summary>Bytes in so far, when the provider counts them. Null leaves the size line off.</summary>
    public long? BytesReceived { get; init; }

    /// <summary>Expected total, when the provider knows it up front.</summary>
    public long? TotalBytes { get; init; }

    /// <summary>Why it ended this way; null for a simple finish, and never a stack trace.</summary>
    public string? Reason { get; init; }
}
