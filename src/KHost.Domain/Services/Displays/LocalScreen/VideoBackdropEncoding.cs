using System.Diagnostics;
using FFMpegCore;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.Displays.LocalScreen;

/// <summary>What a video backdrop's file is made of, as far as deciding whether the screen plays it
/// as it is.</summary>
/// <param name="FormatName">The container, as ffprobe names it (<c>mov,mp4,m4a,3gp,3g2,mj2</c>).</param>
/// <param name="VideoCodec">The first moving picture's codec; null when there is none.</param>
/// <param name="PixelFormat">That picture's pixel format, as ffprobe names it (<c>yuv420p</c>); null when
/// there is no picture or it was not reported.</param>
/// <param name="AudioCodec">The first sound's codec; null when there is none.</param>
public sealed record VideoBackdropFacts(string FormatName, string? VideoCodec, string? PixelFormat, string? AudioCodec);

/// <summary>Reads a video backdrop's <see cref="VideoBackdropFacts"/>.</summary>
public interface IVideoBackdropProbe
{
    /// <summary>The file's facts; null when it could not be read. Throws only when cancelled.</summary>
    Task<VideoBackdropFacts?> ProbeAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>Encodes a video backdrop into something the screen plays.</summary>
public interface IVideoBackdropEncoder
{
    /// <summary>Writes <paramref name="source"/>'s picture to <paramref name="output"/> as H.264 in
    /// MP4; false when it could not.</summary>
    Task<bool> EncodeAsync(string source, string output, CancellationToken cancellationToken = default);
}

/// <summary>Asks ffprobe.</summary>
public sealed class FfprobeVideoBackdropProbe(ILogger<FfprobeVideoBackdropProbe> logger) : IVideoBackdropProbe
{
    public async Task<VideoBackdropFacts?> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var analysis = await FFProbe.AnalyseAsync(path, cancellationToken: cancellationToken);

            // A cover image muxed as a video stream is not the picture that plays.
            var video = analysis.VideoStreams.FirstOrDefault(FfprobeSourcePictureProbe.IsMovingPicture);

            return new VideoBackdropFacts(analysis.Format.FormatName ?? string.Empty, video?.CodecName,
                video?.PixelFormat, analysis.PrimaryAudioStream?.CodecName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read '{FilePath}' to draw it under the words", path);
            return null;
        }
    }
}

/// <summary>Runs ffmpeg.</summary>
public sealed class FfmpegVideoBackdropEncoder(ILogger<FfmpegVideoBackdropEncoder> logger, IFFmpegService ffmpeg)
    : IVideoBackdropEncoder
{
    /// <summary>The longest stretch encoded; the screen loops whatever there is.</summary>
    internal const int MaxSeconds = 600;

    public async Task<bool> EncodeAsync(string source, string output, CancellationToken cancellationToken = default)
    {
        // Found afresh, so an install mid-show counts.
        if (ffmpeg.Locate(FFmpegTool.FFmpeg) is not { } ffmpegPath)
        {
            logger.LogWarning("Could not encode '{FilePath}' for the screen: FFmpeg is not installed", source);
            return false;
        }

        var start = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in BuildArguments(source, output)) start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("ffmpeg did not start.");

        // Behind the song's own encode and the screen for the CPU.
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
        catch { /* not every platform lets a process lower another */ }

        await using var kill = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { /* already gone */ }
        });

        var errors = await process.StandardError.ReadToEndAsync(CancellationToken.None);
        await process.WaitForExitAsync(CancellationToken.None);
        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode == 0) return true;

        logger.LogWarning("Could not encode '{FilePath}' for the screen: {Error}", source, errors.Trim());
        return false;
    }

    /// <summary>Picture only, H.264 High in yuv420p fitted inside 1280x720 at even dimensions, capped
    /// at <see cref="MaxSeconds"/>, moov first so playback starts before the whole file is read.</summary>
    internal static IReadOnlyList<string> BuildArguments(string source, string output) =>
    [
        "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
        "-i", source,
        // 0:V leaves out attached pictures, which would otherwise be the first video stream.
        "-map", "0:V:0",
        "-an", "-sn", "-dn",
        "-t", MaxSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "-vf", "scale=w=1280:h=720:force_original_aspect_ratio=decrease,scale=trunc(iw/2)*2:trunc(ih/2)*2",
        "-c:v", "libx264", "-preset", "veryfast", "-profile:v", "high", "-pix_fmt", "yuv420p",
        "-movflags", "+faststart",
        // Named, because the output's extension is the temporary one.
        "-f", "mp4",
        output,
    ];
}
