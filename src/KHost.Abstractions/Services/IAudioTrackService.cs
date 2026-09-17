using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Finds the separately-mixable voices in a file; usually empty for one-stream files.</summary>
public interface IAudioTrackService
{
    /// <summary>Empty when there's nothing to report; never worth failing a load over.</summary>
    Task<IReadOnlyList<AudioTrack>> ReadTracksAsync(string filePath, CancellationToken cancellationToken = default);
}
