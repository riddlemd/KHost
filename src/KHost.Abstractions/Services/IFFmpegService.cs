using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Finds the ffmpeg and ffprobe the host runs, and installs a pinned build when asked.</summary>
/// <remarks>Looked for in this order: the folder a host set in App Settings, then
/// <see cref="IHostDirectories.BinDirectory"/>, then the system PATH. A plugin that runs either
/// program itself asks <see cref="Locate"/> rather than naming it bare, so it runs the same copy the
/// host does. A host singleton, callable from any thread. Announces
/// <see cref="KHost.Abstractions.Messaging.Messages.FFmpegChanged"/> whenever
/// <see cref="Status"/> moves.</remarks>
public interface IFFmpegService
{
    /// <summary>What the last check found, and the latest install's progress.</summary>
    FFmpegStatus Status { get; }

    /// <summary>Whether a build is pinned for this machine's platform, so
    /// <see cref="InstallAsync"/> has something to fetch.</summary>
    bool CanInstall { get; }

    /// <summary>The executable the host would run now, looked for afresh; null when there is none.</summary>
    /// <remarks>Cheap: it looks for the file and does not run it, so it may name a program that
    /// then fails to start.</remarks>
    string? Locate(FFmpegTool tool);

    /// <summary>Looks for both programs again and asks each for its version.</summary>
    /// <returns>The new <see cref="Status"/>.</returns>
    Task<FFmpegStatus> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads the pinned build into <see cref="IHostDirectories.BinDirectory"/> and
    /// uses it from the next song on, without a restart.</summary>
    /// <returns>The status once it ends. A failure comes back in
    /// <see cref="FFmpegStatus.Install"/> rather than as an exception, and leaves whatever was
    /// installed before in place. While an install is already running, its current status is
    /// returned and nothing new starts.</returns>
    Task<FFmpegStatus> InstallAsync(CancellationToken cancellationToken = default);
}
