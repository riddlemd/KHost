using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace KHost.Screen.FFmpeg;

/// <summary>
/// Runs a locally installed ffmpeg/ffprobe executable via <see cref="Process"/>.
/// </summary>
/// <remarks>
/// Discovery order (first match wins):
/// <list type="number">
///   <item>Explicit directory passed to the constructor.</item>
///   <item>The <c>FFMPEG_PATH</c> environment variable (directory or full path).</item>
///   <item>The system <c>PATH</c>.</item>
/// </list>
/// On Windows the <c>.exe</c> extension is appended automatically; on Linux/macOS it is not.
/// </remarks>
public sealed class FfmpegService : IFfmpegService
{
    private static readonly Regex ProgressRegex = new(
        @"frame=\s*(?<frame>\d+).*?fps=\s*(?<fps>[\d.]+).*?time=(?<time>\d{2}:\d{2}:\d{2}\.\d{2}).*?bitrate=\s*(?<bitrate>[\d.]+)kbits/s.*?speed=\s*(?<speed>[\d.]+)x",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string FfmpegPath { get; }
    public string FfprobePath { get; }
    public bool IsAvailable { get; }

    /// <param name="ffmpegDirectory">
    /// Optional directory containing the ffmpeg executables.
    /// Pass null to auto-discover via <c>FFMPEG_PATH</c> env var or system PATH.
    /// </param>
    public FfmpegService(string? ffmpegDirectory = null)
    {
        FfmpegPath = ResolveExecutable("ffmpeg", ffmpegDirectory);
        FfprobePath = ResolveExecutable("ffprobe", ffmpegDirectory);
        IsAvailable = File.Exists(FfmpegPath);
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        var result = await RunAsync("-version", cancellationToken).ConfigureAwait(false);
        // ffmpeg prints version to stderr
        return (result.Error.Length > 0 ? result.Error : result.Output).Split('\n')[0].Trim();
    }

    public Task<FfmpegResult> RunAsync(string arguments, CancellationToken cancellationToken = default)
        => RunAsync(arguments, progress: null, cancellationToken);

    public async Task<FfmpegResult> RunAsync(
        string arguments,
        IProgress<FfmpegProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var psi = new ProcessStartInfo(FfmpegPath, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderr.AppendLine(e.Data);
            if (progress is not null) TryReportProgress(e.Data, progress);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await WaitForExitAsync(process, cancellationToken).ConfigureAwait(false);

        return new FfmpegResult(
            process.ExitCode == 0,
            process.ExitCode,
            stdout.ToString(),
            stderr.ToString());
    }

    public async Task<string> ProbeAsync(
        string inputPath,
        string? probeArguments = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FfprobePath))
            throw new InvalidOperationException($"ffprobe not found at: {FfprobePath}");

        probeArguments ??= "-v quiet -print_format json -show_format -show_streams";

        var psi = new ProcessStartInfo(FfprobePath, $"{probeArguments} \"{inputPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        string output = await process.StandardOutput
            .ReadToEndAsync(cancellationToken)
            .ConfigureAwait(false);

        await WaitForExitAsync(process, cancellationToken).ConfigureAwait(false);
        return output;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void EnsureAvailable()
    {
        if (!IsAvailable)
            throw new InvalidOperationException(
                $"ffmpeg executable not found. Searched: {FfmpegPath}. " +
                "Install ffmpeg and ensure it is on the system PATH, " +
                "or set the FFMPEG_PATH environment variable.");
    }

    private static string ResolveExecutable(string name, string? explicitDirectory)
    {
        // Append .exe only on Windows
        string exeName = OperatingSystem.IsWindows() ? name + ".exe" : name;

        if (explicitDirectory is not null)
            return Path.Combine(explicitDirectory, exeName);

        string? envPath = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            if (Directory.Exists(envPath))
                return Path.Combine(envPath, exeName);

            if (File.Exists(envPath) &&
                Path.GetFileName(envPath).Equals(exeName, StringComparison.OrdinalIgnoreCase))
                return envPath;

            string? dir = Path.GetDirectoryName(envPath);
            if (dir is not null)
                return Path.Combine(dir, exeName);
        }

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv is not null)
        {
            foreach (string segment in pathEnv.Split(Path.PathSeparator))
            {
                string candidate = Path.Combine(segment.Trim(), exeName);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return exeName; // best-guess; IsAvailable will be false
    }

    private static void TryReportProgress(string line, IProgress<FfmpegProgress> progress)
    {
        var m = ProgressRegex.Match(line);
        if (!m.Success) return;

        TimeSpan? time = TimeSpan.TryParse(m.Groups["time"].Value, out var t) ? t : null;
        long? frame = long.TryParse(m.Groups["frame"].Value, out var f) ? f : null;
        double? fps = TryParseDouble(m.Groups["fps"].Value);
        double? speed = TryParseDouble(m.Groups["speed"].Value);
        double? bitrate = TryParseDouble(m.Groups["bitrate"].Value);

        progress.Report(new FfmpegProgress
        {
            CurrentTime = time,
            Frame = frame,
            Fps = fps,
            Speed = speed,
            BitrateKbps = bitrate,
        });
    }

    private static double? TryParseDouble(string s) =>
        double.TryParse(s,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var v) ? v : null;

    private static async Task WaitForExitAsync(Process process, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => tcs.TrySetResult(true);

        if (process.HasExited) tcs.TrySetResult(true);

        using var reg = cancellationToken.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            tcs.TrySetCanceled(cancellationToken);
        });

        await tcs.Task.ConfigureAwait(false);
    }
}
