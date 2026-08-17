using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>
/// Owns transcoding on the host. Screens consume the resulting URL over HTTP, which is what lets
/// a screen run without ffmpeg and without access to the media library.
/// </summary>
public interface IMediaStreamService
{
    /// <summary>
    /// Starts a transcode of <paramref name="filePath"/>. <paramref name="startOffset"/> is where
    /// the stream begins within the song; consumers seek inside the stream on their own.
    /// </summary>
    Task<MediaStreamSession> OpenAsync(
        string filePath,
        TimeSpan startOffset = default,
        int pitchSemitones = 0,
        CancellationToken cancellationToken = default);

    /// <summary>Stops the transcode and discards its segments. Unknown ids are ignored.</summary>
    Task CloseAsync(string sessionId);

    Task CloseAllAsync();

    /// <summary>
    /// Absolute path of a playlist or segment inside a session, or null when the session or file
    /// is unknown. Returning null rather than throwing keeps the HTTP layer a plain 404.
    /// </summary>
    string? ResolveArtifact(string sessionId, string fileName);
}
