using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IAnalyticsService
{
    void RecordMediaParseDuration(double milliseconds);
    void RecordImportDuration(double milliseconds);
    void RecordCacheSaveDuration(double milliseconds, string key);
    void RecordCacheLoadDuration(double milliseconds, string key, bool hit);
    void RecordImportFilesProcessed(long count, string outcome);
    void RecordQueueMutation();
    void RecordPlaybackStateTransition(PlaybackState toState);
    IAnalyticsActivity StartActivity(string name);
}
