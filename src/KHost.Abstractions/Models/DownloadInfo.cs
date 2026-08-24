namespace KHost.Abstractions.Models;

public enum DownloadState { Downloading, Completed, Failed, Cancelled }

/// <summary>A snapshot of one plugin download, active or settled, for the Downloads management page.</summary>
public sealed record DownloadInfo
{
    public required Guid MediaId { get; init; }
    public required string Title { get; init; }
    public string Artist { get; init; } = string.Empty;

    /// <summary>The provider's display name. Empty when the caller did not supply one.</summary>
    public string Source { get; init; } = string.Empty;

    public required DateTime StartedUtc { get; init; }
    public DownloadState State { get; init; } = DownloadState.Downloading;

    /// <summary>0..1, or null while unreported — rendered as an indeterminate progress bar.</summary>
    public double? Progress { get; init; }
}
