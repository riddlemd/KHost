using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Owns encoding, so a display needs neither an encoder nor access to the library.</summary>
/// <remarks>Opens per-song sessions, each a served directory addressable over HTTP by any number of
/// consumers, and removed with everything in it when the session closes. A plugin renderer takes it
/// to encode a file or to get somewhere to write its own; a display provider only ever receives the
/// URLs. Before encoding, a file only a plugin can read is first turned into a playable one through
/// <see cref="IPlayableMediaSourceService"/>. The host's settings for it apply to the next session
/// opened, without a restart. A host singleton, callable from any thread. Announces
/// nothing.</remarks>
public interface IMediaStreamService
{
    /// <summary>Starts encoding <paramref name="filePath"/> into a stream and returns once it is
    /// ready to fetch.</summary>
    /// <param name="filePath">The media file to stream.</param>
    /// <param name="startOffset">Song position the stream begins at; its zero maps here.</param>
    /// <param name="pitch">Semitones from the written key.</param>
    /// <param name="tempo">Percent either side of recorded speed.</param>
    /// <param name="mix">Levels for a song with separate voices; null leaves the file's own mix.</param>
    /// <param name="cancellationToken">Abandons the open.</param>
    /// <remarks>Pitch, tempo and mix are fixed for the session's lifetime; changing any of them means
    /// opening a new session. The caller owns the session and must close it.</remarks>
    /// <exception cref="FileNotFoundException">When nothing is at <paramref name="filePath"/>.</exception>
    /// <exception cref="InvalidOperationException">When the encode cannot start or produces nothing
    /// to play.</exception>
    /// <exception cref="OperationCanceledException">When <paramref name="cancellationToken"/> fires;
    /// the half-open session is closed first.</exception>
    Task<MediaStreamSession> OpenAsync(
        string filePath,
        TimeSpan startOffset = default,
        int pitch = 0,
        int tempo = 0,
        AudioMix? mix = null,
        CancellationToken cancellationToken = default);

    /// <summary>A served, swept directory with no encode in it.</summary>
    /// <remarks>For a renderer that produces its own files — stems demuxed out of a container, say
    /// — and needs somewhere the display can fetch them from that is cleaned up with the song. The
    /// session it returns carries no <see cref="MediaStreamSession.PlaylistUrl"/>, and is closed
    /// like any other; build each file's address with <see cref="BuildArtifactUrl"/>.</remarks>
    /// <exception cref="FileNotFoundException">When nothing is at <paramref name="filePath"/>.</exception>
    Task<MediaStreamSession> OpenWithoutEncodeAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>Ends a session and deletes its directory. Unknown ids are ignored.</summary>
    Task CloseAsync(string sessionId);

    /// <summary>Ends every open session; the host calls it on shutdown.</summary>
    Task CloseAllAsync();

    /// <summary>The file on disk a consumer's request for <paramref name="fileName"/> in a session
    /// resolves to.</summary>
    /// <returns>Null rather than throwing, so the HTTP layer stays a plain 404: for an unknown
    /// session, a missing file, or a name that is not a bare file name.</returns>
    string? ResolveArtifact(string sessionId, string fileName);

    /// <summary>Where a consumer fetches one file a renderer wrote into a session's directory.</summary>
    /// <remarks>Built here so the route's shape stays in one place rather than in every renderer
    /// that produces files of its own.</remarks>
    string BuildArtifactUrl(string sessionId, string fileName);

    /// <summary>Where a display fetches the picture of a library still, by its media id.</summary>
    string BuildImageUrl(Guid mediaId);
}
