using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Records the host's own metrics and trace spans.</summary>
/// <remarks>Host-only instrumentation: each metric is fixed to one of the host's own operations
/// (imports, cache, queue, playback), so a plugin has little reason to take it. A host singleton,
/// callable from any thread; every call returns at once, never throws, and does nothing when
/// telemetry is not being collected.</remarks>
public interface IAnalyticsService
{
    /// <summary>Time taken to read one media file's metadata.</summary>
    void RecordMediaParseDuration(double milliseconds);

    /// <summary>Time taken by a whole import run.</summary>
    void RecordImportDuration(double milliseconds);

    /// <summary>Time taken to write one <see cref="ICacheService"/> entry, tagged by key.</summary>
    void RecordCacheSaveDuration(double milliseconds, string key);

    /// <summary>Time taken to read one <see cref="ICacheService"/> entry.</summary>
    /// <param name="milliseconds">How long the read took.</param>
    /// <param name="key">The cache key read, recorded as a tag.</param>
    /// <param name="hit">Whether the read found a value.</param>
    void RecordCacheLoadDuration(double milliseconds, string key, bool hit);

    /// <summary>Counts files an import finished with, tagged by how each ended.</summary>
    void RecordImportFilesProcessed(long count, string outcome);

    /// <summary>Counts one change to the singer queue.</summary>
    void RecordQueueMutation();

    /// <summary>Counts one move into <paramref name="toState"/>.</summary>
    void RecordPlaybackStateTransition(PlaybackState toState);

    /// <summary>Starts a trace span; dispose the result to end it.</summary>
    /// <returns>Never null, even when tracing is off.</returns>
    IAnalyticsActivity StartActivity(string name);
}
