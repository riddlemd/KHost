using System.Collections.Concurrent;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;

namespace KHost.Domain.Services;

// Dependency-free like the registry it replaces: PluginLibrary depends on IPerformanceService, and
// PerformanceService depends on IDownloadsService (to cancel a download on dequeue), so this has to
// carry no dependencies of its own to avoid a constructor cycle between the two. IMessageBroker has
// no dependencies of its own, so it is safe to inject here.
public class DownloadsService : IDownloadsService
{
    private const int RecentCap = 50;

    private readonly ConcurrentDictionary<Guid, ActiveEntry> _active = new();

    // Newest first; trimmed to RecentCap under _recentLock, which guards nothing but this list —
    // everything else here is already safe under ConcurrentDictionary's own atomics.
    private readonly List<DownloadInfo> _recent = [];
    private readonly object _recentLock = new();
    private readonly IMessageBroker _broker;


    public DownloadsService(IMessageBroker broker)
    {
        _broker = broker;
    }

    public IReadOnlyList<DownloadInfo> Snapshot()
    {
        List<DownloadInfo> recent;
        lock (_recentLock)
            recent = [.. _recent];

        return
        [
            .. _active.Values.Select(e => e.Info).OrderByDescending(i => i.StartedUtc),
            .. recent,
        ];
    }

    public CancellationToken Register(Guid mediaId, string title, string artist, string source)
    {
        var entry = new ActiveEntry
        {
            Cts = new CancellationTokenSource(),
            Info = new DownloadInfo
            {
                MediaId = mediaId,
                Title = title,
                Artist = artist,
                Source = source,
                StartedUtc = DateTime.UtcNow,
                State = DownloadState.Downloading,
            },
        };

        _active[mediaId] = entry;
        RaiseStateChanged();

        return entry.Cts.Token;
    }

    public CancellationToken TokenForInFlight(Guid mediaId, string title, string artist, string source) =>
        _active.TryGetValue(mediaId, out var entry) ? entry.Cts.Token : Register(mediaId, title, artist, source);

    public void ReportProgress(Guid mediaId, double fraction)
    {
        if (!_active.TryGetValue(mediaId, out var entry)) return;

        var clamped = Math.Clamp(fraction, 0d, 1d);
        var previousPercent = entry.Info.Progress is { } previous ? (int)(previous * 100) : -1;
        var newPercent = (int)(clamped * 100);

        entry.Info = entry.Info with { Progress = clamped };

        // Raised only on an integer-percent change: a plugin can report every few milliseconds,
        // and a DownloadsChanged per call is the chatty-event-stream failure mode this repo has
        // already lived through once.
        if (newPercent != previousPercent)
            RaiseStateChanged();
    }

    public void Settle(Guid mediaId, DownloadState state)
    {
        if (state == DownloadState.Downloading) return;
        if (!_active.TryRemove(mediaId, out var entry)) return;

        entry.Cts.Dispose();
        AddToRecent(entry.Info with { State = state });
        RaiseStateChanged();
    }

    public Task CancelAsync(Guid mediaId)
    {
        if (_active.TryRemove(mediaId, out var entry))
        {
            entry.Cts.Cancel();
            entry.Cts.Dispose();
            AddToRecent(entry.Info with { State = DownloadState.Cancelled });
            RaiseStateChanged();
        }

        return Task.CompletedTask;
    }

    public void CancelAll()
    {
        var ids = _active.Keys.ToArray();
        var any = false;

        foreach (var id in ids)
        {
            if (!_active.TryRemove(id, out var entry)) continue;

            entry.Cts.Cancel();
            entry.Cts.Dispose();
            AddToRecent(entry.Info with { State = DownloadState.Cancelled });
            any = true;
        }

        if (any)
            RaiseStateChanged();
    }

    private void AddToRecent(DownloadInfo info)
    {
        lock (_recentLock)
        {
            _recent.Insert(0, info);
            if (_recent.Count > RecentCap)
                _recent.RemoveRange(RecentCap, _recent.Count - RecentCap);
        }
    }

    private void RaiseStateChanged()
    {
        if (_broker is { } broker)
            _ = broker.PublishAsync(new DownloadsChanged());
    }

    private sealed class ActiveEntry
    {
        public required CancellationTokenSource Cts { get; init; }
        public required DownloadInfo Info { get; set; }
    }
}
