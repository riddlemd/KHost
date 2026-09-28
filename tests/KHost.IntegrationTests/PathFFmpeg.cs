using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

namespace KHost.IntegrationTests;

/// <summary>The FFmpeg these tests already rely on: whatever is on PATH, named bare.</summary>
/// <remarks>Only <see cref="Locate"/> is meant to be reached; the tests that use it never check or
/// install.</remarks>
internal sealed class PathFFmpeg : IFFmpegService
{
    public FFmpegStatus Status => throw new NotSupportedException();

    public bool CanInstall => false;

    public string? Locate(FFmpegTool tool) => tool == FFmpegTool.FFmpeg ? "ffmpeg" : "ffprobe";

    public Task<FFmpegStatus> CheckAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<FFmpegStatus> InstallAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
