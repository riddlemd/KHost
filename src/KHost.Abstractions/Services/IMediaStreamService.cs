using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Owns transcoding, so a screen needs neither ffmpeg nor library access.</summary>
public interface IMediaStreamService
{
    /// <summary>Pitch, tempo and mix are fixed for the session's lifetime.</summary>
    Task<MediaStreamSession> OpenAsync(
        string filePath,
        TimeSpan startOffset = default,
        int pitch = 0,
        int tempo = 0,
        AudioMix? mix = null,
        CancellationToken cancellationToken = default);

    /// <summary>Unknown ids are ignored.</summary>
    Task CloseAsync(string sessionId);

    Task CloseAllAsync();

    /// <summary>Null rather than throwing, so the HTTP layer stays a plain 404.</summary>
    string? ResolveArtifact(string sessionId, string fileName);

    /// <summary>Where a screen fetches a library still; this service already owns that address.</summary>
    string BuildImageUrl(Guid mediaId);
}
