using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>
/// Finds the separately-mixable voices in a media file. Most karaoke files carry one audio
/// stream and have none, which is why the answer is usually empty.
/// </summary>
public interface IAudioTrackService
{
    /// <summary>
    /// The named audio tracks in <paramref name="filePath"/>, or empty when it has one stream,
    /// when the names say nothing, or when the file cannot be read — none of which is an error
    /// worth failing a load over, since the song plays either way.
    /// </summary>
    Task<IReadOnlyList<AudioTrack>> ReadTracksAsync(string filePath, CancellationToken cancellationToken = default);
}
