namespace KHost.Abstractions.Models;

/// <summary>The two programs the host plays and describes media with.</summary>
public enum FFmpegTool
{
    /// <summary>The encoder every song streams through.</summary>
    FFmpeg,

    /// <summary>The reader that gives an imported file its length, tracks and tags.</summary>
    FFprobe,
}

/// <summary>Where one of the two programs was found, and whether it ran.</summary>
public sealed record FFmpegToolStatus
{
    /// <summary>Which program this describes.</summary>
    public required FFmpegTool Tool { get; init; }

    /// <summary>The executable the host would run, or null when none was found anywhere it looks.</summary>
    public string? Path { get; init; }

    /// <summary>The version the program reported for itself, or null when it did not run.</summary>
    public string? Version { get; init; }

    /// <summary>Why a program that was found did not run, in words a host can act on.</summary>
    public string? Error { get; init; }

    /// <summary>Found and ran: the host can use it.</summary>
    public bool IsUsable => Path is not null && Version is not null;
}

/// <summary>How far an install of the pinned FFmpeg build has got.</summary>
public enum FFmpegInstallState
{
    /// <summary>No install has run in this process.</summary>
    None,

    /// <summary>Fetching the pinned archives.</summary>
    Downloading,

    /// <summary>Checking what arrived against the pinned checksums and unpacking it.</summary>
    Verifying,

    /// <summary>Running the unpacked programs and putting them in place.</summary>
    Installing,

    /// <summary>Installed and in use.</summary>
    Succeeded,

    /// <summary>Stopped, with <see cref="FFmpegInstallProgress.Error"/> saying why; nothing was replaced.</summary>
    Failed,
}

/// <summary>The latest install this process ran.</summary>
public sealed record FFmpegInstallProgress
{
    /// <summary>The stage it has reached, or how it ended.</summary>
    public FFmpegInstallState State { get; init; }

    /// <summary>0 to 1 while downloading; null when there is nothing to measure.</summary>
    public double? Progress { get; init; }

    /// <summary>Why a failed install stopped, in words a host can act on.</summary>
    public string? Error { get; init; }

    /// <summary>Whether an install is running now.</summary>
    public bool IsRunning => State is FFmpegInstallState.Downloading
        or FFmpegInstallState.Verifying
        or FFmpegInstallState.Installing;
}

/// <summary>What the host last found out about ffmpeg and ffprobe.</summary>
public sealed record FFmpegStatus
{
    /// <summary>The encoder.</summary>
    public required FFmpegToolStatus FFmpeg { get; init; }

    /// <summary>The reader.</summary>
    public required FFmpegToolStatus FFprobe { get; init; }

    /// <summary>The latest install, or <see cref="FFmpegInstallState.None"/>.</summary>
    public FFmpegInstallProgress Install { get; init; } = new();

    /// <summary>False until the first check has finished; the two tools read as missing until then.</summary>
    public bool HasChecked { get; init; }

    /// <summary>Both programs were found and ran.</summary>
    public bool IsReady => FFmpeg.IsUsable && FFprobe.IsUsable;
}
