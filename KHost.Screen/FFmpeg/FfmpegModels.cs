namespace KHost.Screen.FFmpeg;

/// <summary>The outcome of an ffmpeg process invocation.</summary>
public sealed class FfmpegResult
{
    public bool Success { get; }
    public int ExitCode { get; }
    /// <summary>Text written to stdout (typically empty for encoding operations).</summary>
    public string Output { get; }
    /// <summary>Text written to stderr (where ffmpeg writes progress and errors).</summary>
    public string Error { get; }

    public FfmpegResult(bool success, int exitCode, string output, string error)
    {
        Success = success;
        ExitCode = exitCode;
        Output = output;
        Error = error;
    }

    public override string ToString() =>
        Success
            ? $"OK (exit {ExitCode})"
            : $"FAILED (exit {ExitCode}): {Error.Split('\n')[^1].Trim()}";
}

/// <summary>
/// A single progress update parsed from ffmpeg's stderr output.
/// Fields are null when ffmpeg did not emit a value for that line.
/// </summary>
public sealed class FfmpegProgress
{
    /// <summary>Current encode position.</summary>
    public TimeSpan? CurrentTime { get; init; }
    /// <summary>Frames processed so far.</summary>
    public long? Frame { get; init; }
    /// <summary>Frames per second at the current encode rate.</summary>
    public double? Fps { get; init; }
    /// <summary>Encode speed relative to real-time.</summary>
    public double? Speed { get; init; }
    /// <summary>Current output bitrate in kbits/s.</summary>
    public double? BitrateKbps { get; init; }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Frame.HasValue) parts.Add($"frame={Frame}");
        if (Fps.HasValue) parts.Add($"fps={Fps:F1}");
        if (CurrentTime.HasValue) parts.Add($"time={CurrentTime:hh\\:mm\\:ss\\.ff}");
        if (Speed.HasValue) parts.Add($"speed={Speed:F2}x");
        return parts.Count > 0 ? string.Join(" ", parts) : "(progress)";
    }
}
