using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>
/// Owns transcoding on the host, so a screen needs neither ffmpeg nor access to the library.
/// </summary>
public interface IMediaStreamService
{
    /// <summary><paramref name="startOffset"/> is where the stream begins within the song.</summary>
    Task<MediaStreamSession> OpenAsync(
        string filePath,
        TimeSpan startOffset = default,
        int pitchSemitones = 0,
        CancellationToken cancellationToken = default);

    /// <summary>Unknown ids are ignored.</summary>
    Task CloseAsync(string sessionId);

    Task CloseAllAsync();

    /// <summary>Null rather than throwing, so the HTTP layer stays a plain 404.</summary>
    string? ResolveArtifact(string sessionId, string fileName);
}
