using KHost.Abstractions.Services;

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

/// <summary>Asks whichever probe owns the file, so a plugin's container is never handed to ffprobe.
/// </summary>
public sealed class MediaSourcePictureProbe(IMediaProbeService probes) : ISourcePictureProbe
{
    public async Task<bool> HasMovingPictureAsync(string sourcePath, CancellationToken cancellationToken = default)
        => (await probes.ProbeAsync(sourcePath, cancellationToken))?.HasMovingPicture ?? false;
}
