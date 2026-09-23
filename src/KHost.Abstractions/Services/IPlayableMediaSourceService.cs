namespace KHost.Abstractions.Services;

/// <summary>Asks whichever source can make this file playable, so no caller has to know which.
/// </summary>
public interface IPlayableMediaSourceService
{
    /// <summary>The path to open: the original for almost everything, a converted copy for a file
    /// only its provider can read.</summary>
    /// <remarks>Never null — a caller always gets something to open. The common answer is the path
    /// it passed in, since nothing claims an mp4 or a CDG.</remarks>
    Task<string> ResolvePlayableAsync(
        string filePath,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>The separate stems behind a resolved file, in audio-track order; empty for most.</summary>
    /// <param name="filePath">The original, which is what says who resolved it.</param>
    /// <param name="resolvedPath">What <see cref="ResolvePlayableAsync"/> returned for it.</param>
    /// <remarks>Asked after resolving, not instead of it — the stems are a by-product of the same
    /// work and are only worth naming to a display that mixes. A default body so nothing already
    /// implementing this interface has to change.</remarks>
    IReadOnlyList<string> StemsOf(string filePath, string resolvedPath) => [];
}
