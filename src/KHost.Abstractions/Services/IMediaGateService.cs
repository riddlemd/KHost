using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Routes by <see cref="IMediaPlaybackGate.MetadataTag"/>; unclaimed media always plays.</summary>
public interface IMediaGateService
{
    Task<PlaybackGateResult> EvaluateAsync(Media media, CancellationToken cancellationToken = default);
}
