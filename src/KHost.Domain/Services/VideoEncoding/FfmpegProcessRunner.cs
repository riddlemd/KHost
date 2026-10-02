using System.Diagnostics;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

namespace KHost.Domain.Services.VideoEncoding;

/// <summary>What one finished ffmpeg run said.</summary>
internal sealed record FfmpegRun(int ExitCode, string Output, string Error);

/// <summary>Runs ffmpeg to completion in a directory; the seam the encoder probe is tested through.</summary>
internal interface IFfmpegProcessRunner
{
    /// <summary>The ffmpeg a song would be encoded with now, or null when there is none.</summary>
    string? Locate();

    Task<FfmpegRun> RunAsync(
        string ffmpegPath, string arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken);
}

internal sealed class FfmpegProcessRunner : IFfmpegProcessRunner
{
    private readonly Func<string?> _locate;

    /// <summary>Through the host's own lookup, so the probe tests the copy a song then runs.</summary>
    public FfmpegProcessRunner(IFFmpegService ffmpeg)
        : this(() => ffmpeg.Locate(FFmpegTool.FFmpeg))
    {
    }

    internal FfmpegProcessRunner(Func<string?> locate) => _locate = locate;

    public string? Locate() => _locate();

    public async Task<FfmpegRun> RunAsync(
        string ffmpegPath, string arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = Process.Start(new ProcessStartInfo(ffmpegPath, arguments)
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
}
