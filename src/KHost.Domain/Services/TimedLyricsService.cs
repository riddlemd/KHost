using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

/// <inheritdoc cref="ITimedLyricsService"/>
public sealed class TimedLyricsService(
    ILogger<TimedLyricsService> logger,
    IEnumerable<ITimedLyricsProvider> providers) : ITimedLyricsService
{
    private readonly IReadOnlyList<ITimedLyricsProvider> _providers = [.. providers];

    public async Task<TimedLyrics?> GetTimedLyricsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        foreach (var provider in _providers)
        {
            bool claimed;

            // A provider that throws deciding is skipped rather than fatal: the next one may own
            // the file, and a song must still load when a plugin is having a bad day.
            try { claimed = provider.CanProvide(filePath); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "A lyric provider failed deciding whether it owns '{FilePath}'", filePath);
                continue;
            }

            if (!claimed) continue;

            // Answering null once it has claimed the file ends the search: nobody else can read a
            // container its owner could not.
            try { return await provider.GetTimedLyricsAsync(filePath, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "The lyric provider that owns '{FilePath}' could not read it", filePath);
                return null;
            }
        }

        return null;
    }
}
