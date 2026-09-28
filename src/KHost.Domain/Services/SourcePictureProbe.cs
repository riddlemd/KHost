using FFMpegCore;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

/// <summary>Whether a file carries a moving picture of its own, the question
/// <see cref="SongBackdrops.ForPlaying"/> needs answered.</summary>
/// <remarks>Host plumbing, not a contract: it sits with its implementation rather than in
/// Abstractions because no plugin has a reason to ask it.</remarks>
public interface ISourcePictureProbe
{
    /// <summary>True when the file has a video stream that is not an attached picture (cover art).
    /// A file that cannot be read answers false, the same answer the burn-in takes.</summary>
    Task<bool> HasMovingPictureAsync(string sourcePath, CancellationToken cancellationToken = default);
}

/// <summary>Asks ffprobe.</summary>
public sealed class FfprobeSourcePictureProbe(ILogger<FfprobeSourcePictureProbe> logger) : ISourcePictureProbe
{
    public async Task<bool> HasMovingPictureAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var analysis = await FFProbe.AnalyseAsync(sourcePath, cancellationToken: cancellationToken);
            return analysis.VideoStreams.Any(IsMovingPicture);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogInformation(ex, "Could not probe '{FilePath}' for a picture; taking it to have none", sourcePath);
            return false;
        }
    }

    /// <summary>A cover image muxed as a video stream is one frame, not a picture to play under words.</summary>
    internal static bool IsMovingPicture(VideoStream stream)
        => stream.Disposition?.GetValueOrDefault("attached_pic") != true;
}
