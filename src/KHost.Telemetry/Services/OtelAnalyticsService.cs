using KHost.Abstractions.Services;

namespace KHost.Telemetry.Services;

internal sealed class OtelAnalyticsService : IAnalyticsService
{
    public void RecordMediaParseDuration(double milliseconds) =>
        KHostMetrics.MediaParseDuration.Record(milliseconds);

    public void RecordImportDuration(double milliseconds) =>
        KHostMetrics.ImportDuration.Record(milliseconds);

    public void RecordCacheSaveDuration(double milliseconds, string key) =>
        KHostMetrics.CacheSaveDuration.Record(milliseconds,
            new KeyValuePair<string, object?>("key", key));

    public void RecordCacheLoadDuration(double milliseconds, string key, bool hit) =>
        KHostMetrics.CacheLoadDuration.Record(milliseconds,
            new KeyValuePair<string, object?>("key", key),
            new KeyValuePair<string, object?>("hit", hit));

    public void RecordImportFilesProcessed(long count, string outcome) =>
        KHostMetrics.ImportFilesProcessed.Add(count,
            new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordQueueMutation() =>
        KHostMetrics.QueueMutation.Add(1);

    public void RecordPlaybackStateTransition(PlaybackState toState) =>
        KHostMetrics.PlaybackStateTransition.Add(1,
            new KeyValuePair<string, object?>("to", toState.ToString().ToLowerInvariant()));

    public IAnalyticsActivity StartActivity(string name) =>
        new OtelAnalyticsActivity(KHostActivitySource.Source.StartActivity(name));
}
