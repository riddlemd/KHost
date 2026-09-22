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
}
