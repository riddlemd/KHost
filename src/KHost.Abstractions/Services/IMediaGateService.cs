using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Asks the one gate that owns a file, so no caller has to know which that is.</summary>
/// <remarks>Ownership is the tag when a file carries one, and <see cref="IMediaPlaybackGate.Claims"/>
/// for a container that has nowhere to put one. Media nobody owns always plays.</remarks>
public interface IMediaGateService
{
    Task<PlaybackGateResult> EvaluateAsync(MediaAction action, Media media, CancellationToken cancellationToken = default);
}
