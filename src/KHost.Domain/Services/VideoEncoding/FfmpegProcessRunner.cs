using System.Diagnostics;
using FFMpegCore;

namespace KHost.Domain.Services.VideoEncoding;

/// <summary>What one finished ffmpeg run said.</summary>
internal sealed record FfmpegRun(int ExitCode, string Output, string Error);

/// <summary>Runs ffmpeg to completion in a directory; the seam the encoder probe is tested through.</summary>
internal interface IFfmpegProcessRunner
{
    Task<FfmpegRun> RunAsync(string arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken);
}

internal sealed class FfmpegProcessRunner : IFfmpegProcessRunner
{
    public async Task<FfmpegRun> RunAsync(
        string arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = Process.Start(new ProcessStartInfo(ResolvePath(), arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Failed to start ffmpeg");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        // Both pipes drained together: ffmpeg blocks once either fills.
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            // A driver that hangs opening a session must not outlive the probe.
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            if (cancellationToken.IsCancellationRequested) throw;
            return new FfmpegRun(-1, "", $"timed out after {timeout.TotalSeconds:0}s");
        }

        return new FfmpegRun(process.ExitCode, await output, await error);
    }

    /// <summary>The configured ffmpeg folder when it holds one, else PATH.</summary>
    internal static string ResolvePath()
    {
        var exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        var folder = GlobalFFOptions.Current.BinaryFolder;
        if (!string.IsNullOrEmpty(folder))
        {
            var candidate = Path.Combine(folder, exeName);
            if (File.Exists(candidate)) return candidate;
        }

        return exeName;
    }
}
