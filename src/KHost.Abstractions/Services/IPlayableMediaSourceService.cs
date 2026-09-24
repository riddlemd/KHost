namespace KHost.Abstractions.Services;

/// <summary>Asks whichever source can make this file playable, so no caller has to know which.
/// </summary>
/// <remarks>The router over every <see cref="IPlayableMediaSource"/>, consulted by
/// <see cref="IMediaStreamService.OpenAsync"/> before it encodes. A plugin supplies a source by
/// implementing <see cref="IPlayableMediaSource"/>, not this. A host singleton, callable from any
/// thread. Announces nothing.</remarks>
public interface IPlayableMediaSourceService
{
    /// <summary>The path to open: the original for almost everything, a converted copy for a file
    /// only its provider can read.</summary>
    /// <param name="filePath">The library file about to be streamed.</param>
    /// <param name="workingDirectory">Where a converted copy may be written; it goes when the stream
    /// session that owns it closes.</param>
    /// <param name="cancellationToken">Handed to the claiming source.</param>
    /// <remarks>Never null — a caller always gets something to open. The common answer is the path
    /// it passed in, since nothing claims an mp4 or a CDG. Whatever a claiming source throws while
    /// converting is passed on unchanged, so the failure keeps its cause.</remarks>
    Task<string> ResolvePlayableAsync(
        string filePath,
        string workingDirectory,
        CancellationToken cancellationToken = default);

}
