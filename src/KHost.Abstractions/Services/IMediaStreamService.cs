using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>
/// Owns transcoding on the host, so a screen needs neither ffmpeg nor access to the library.
/// </summary>
public interface IMediaStreamService
{
    /// <summary>
    /// <paramref name="startOffset"/> is where the stream begins within the song.
    /// <paramref name="pitch"/> is in semitones and <paramref name="tempo"/> a percentage either
    /// side of the recorded speed; both are fixed for the session's lifetime.
    /// </summary>
    Task<MediaStreamSession> OpenAsync(
        string filePath,
        TimeSpan startOffset = default,
        int pitch = 0,
        int tempo = 0,
        CancellationToken cancellationToken = default);

    /// <summary>Unknown ids are ignored.</summary>
    Task CloseAsync(string sessionId);

    Task CloseAllAsync();

    /// <summary>Null rather than throwing, so the HTTP layer stays a plain 404.</summary>
    string? ResolveArtifact(string sessionId, string fileName);

    /// <summary>
    /// Where a screen fetches a library still from. It lives here because this service already
    /// owns the address screens reach the host on; a still opens no session and no transcode.
    /// </summary>
    string BuildImageUrl(Guid mediaId);
}
