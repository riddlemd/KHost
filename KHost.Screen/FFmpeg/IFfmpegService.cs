namespace KHost.Screen.FFmpeg;

/// <summary>
/// Provides access to a locally installed ffmpeg/ffprobe executable.
/// </summary>
public interface IFfmpegService
{
    /// <summary>Resolved path to the ffmpeg executable.</summary>
    string FfmpegPath { get; }

    /// <summary>Resolved path to the ffprobe executable.</summary>
    string FfprobePath { get; }

    /// <summary>Returns true if the ffmpeg executable was found and is accessible.</summary>
    bool IsAvailable { get; }

    /// <summary>Returns the version string reported by <c>ffmpeg -version</c>.</summary>
    Task<string> GetVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs ffmpeg with the given argument string and returns the result.</summary>
    Task<FfmpegResult> RunAsync(
        string arguments,
        CancellationToken cancellationToken = default);

    /// <summary>Runs ffmpeg and reports progress during execution.</summary>
    Task<FfmpegResult> RunAsync(
        string arguments,
        IProgress<FfmpegProgress>? progress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs ffprobe on <paramref name="inputPath"/> and returns its output.
    /// Default arguments produce JSON with format and stream info.
    /// </summary>
    Task<string> ProbeAsync(
        string inputPath,
        string? probeArguments = null,
        CancellationToken cancellationToken = default);
}
