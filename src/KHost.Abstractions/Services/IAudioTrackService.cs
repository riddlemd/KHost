using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Finds the separately-mixable voices in a file; usually empty for one-stream files.</summary>
/// <remarks>Policy over <see cref="IMediaProbeService"/>: the probe reports every track, and this
/// keeps only a set worth offering a host as faders. A plugin describes its own tracks by
/// implementing <see cref="IMediaProbe"/>, not this. A host singleton, callable from any thread;
/// each call answers from the file as it is now, so a file replaced under the same path is read
/// afresh.</remarks>
public interface IAudioTrackService
{
    /// <summary>Empty when there's nothing to report; never worth failing a load over.</summary>
    /// <returns>The probed tracks, or empty when the file could not be probed, has fewer than two
    /// audio tracks, or has none roled as music to set the voices against.</returns>
    Task<IReadOnlyList<AudioTrack>> ReadTracksAsync(string filePath, CancellationToken cancellationToken = default);
}
