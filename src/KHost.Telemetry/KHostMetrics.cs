using System.Diagnostics.Metrics;

namespace KHost.Telemetry;

public static class KHostMetrics
{
    private static readonly Meter _meter = new(KHostTelemetry.MeterName, KHostTelemetry.Version);

    public static readonly Histogram<double> MediaParseDuration =
        _meter.CreateHistogram<double>("khost.media.parse.duration", "ms", "FFProbe parse duration per file");

    public static readonly Histogram<double> SearchDuration =
        _meter.CreateHistogram<double>("khost.search.duration", "ms", "Repository search query duration, tagged by entity");

    public static readonly Counter<long> ImportFilesProcessed =
        _meter.CreateCounter<long>("khost.import.files.processed", "{file}", "Files processed during a media import");

    public static readonly Histogram<double> ImportDuration =
        _meter.CreateHistogram<double>("khost.import.duration", "ms", "Total media import run duration");

    public static readonly Histogram<double> CacheSaveDuration =
        _meter.CreateHistogram<double>("khost.cache.save.duration", "ms", "JSON cache save duration");

    public static readonly Histogram<double> CacheLoadDuration =
        _meter.CreateHistogram<double>("khost.cache.load.duration", "ms", "JSON cache load duration");

    public static readonly Counter<long> QueueMutation =
        _meter.CreateCounter<long>("khost.queue.mutation", "{op}", "Singer queue mutation events");

    public static readonly Counter<long> PlaybackStateTransition =
        _meter.CreateCounter<long>("khost.playback.state.transition", "{transition}", "Playback state change events");
}
