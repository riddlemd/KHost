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

    /// <summary>A served, swept directory with no transcode in it.</summary>
    /// <remarks>For a renderer that produces its own files — stems demuxed out of a container, say
    /// — and needs somewhere the display can fetch them from that is cleaned up with the song. The
    /// session it returns carries no <see cref="MediaStreamSession.PlaylistUrl"/>, and is closed
    /// like any other.</remarks>
    Task<MediaStreamSession> OpenWithoutEncodeAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>Unknown ids are ignored.</summary>
    Task CloseAsync(string sessionId);

    Task CloseAllAsync();

    /// <summary>Null rather than throwing, so the HTTP layer stays a plain 404.</summary>
    string? ResolveArtifact(string sessionId, string fileName);

    /// <summary>Where a consumer fetches one file a renderer wrote into a session's directory.</summary>
    /// <remarks>Built here so the route's shape stays in one place rather than in every renderer
    /// that produces files of its own.</remarks>
    string BuildArtifactUrl(string sessionId, string fileName);

    /// <summary>Where a screen fetches a library still; this service already owns that address.</summary>
    string BuildImageUrl(Guid mediaId);
}
