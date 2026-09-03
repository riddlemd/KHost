using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>
/// Reads a file's <see cref="IMediaPlaybackGate.MetadataTag"/> and asks the gate whose
/// <see cref="IMediaPlaybackGate.ProviderId"/> it names whether the media may play. Media with no tag,
/// or a tag no loaded gate claims, is allowed: a gate can refuse its own content, never anyone
/// else's, and an unreadable file is the playback pipeline's problem to report, not this one's.
/// </summary>
public interface IMediaGateService
{
    Task<PlaybackGateResult> EvaluateAsync(Media media, CancellationToken cancellationToken = default);
}
