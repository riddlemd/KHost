using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

/// <inheritdoc cref="IPlayableMediaSourceService"/>
public sealed class PlayableMediaSourceService(
    ILogger<PlayableMediaSourceService> logger,
    IEnumerable<IPlayableMediaSource> sources) : IPlayableMediaSourceService
{
    private readonly IReadOnlyList<IPlayableMediaSource> _sources = [.. sources];

    public async Task<string> ResolvePlayableAsync(
        string filePath,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        foreach (var source in _sources)
        {
            bool claimed;

            // A source that throws deciding is skipped rather than fatal: the next one may own the
            // file, and a song must still start when a plugin is having a bad day.
            try { claimed = source.CanResolve(filePath); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "A playable source failed deciding whether it owns '{FilePath}'", filePath);
                continue;
            }

            if (!claimed) continue;

            // A failure here is not swallowed. The alternative is handing ffmpeg the original,
            // which it cannot read either, and the room gets a song that never starts with nothing
            // in the log naming the cause.
            var resolved = await source.ResolvePlayableAsync(filePath, workingDirectory, cancellationToken);

            if (resolved is not { Length: > 0 }) continue;

            logger.LogDebug("'{FilePath}' will be played from '{Resolved}'", filePath, resolved);
            return resolved;
        }

        return filePath;
    }

}
