using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

public class MediaImportService : BaseService, IMediaImportService
{
    private static readonly string[] _supportedExtensions =
    [
        ".cdg",
        ".mp4",
        ".mkv",
        ".avi",
        ".flv"
    ];

    private readonly IMediaFileParsingService _parser;
    private readonly IMediaRepository _repository;
    private readonly IMediaService _mediaService;
    private readonly IMediaFingerprintService _fingerprints;
    private readonly IAnalyticsService _analytics;

    private CancellationTokenSource? _cts;
    private readonly Lock _startLock = new();
    private long _lastNotifyMs;

    public ImportState State { get; private set; } = ImportState.Idle;
    public int TotalCount { get; private set; }
    public int ImportedCount { get; private set; }
    public int FailedCount { get; private set; }
    public string? CurrentFilePath { get; private set; }
    public IReadOnlyList<string> SupportedExtensions { get; } = _supportedExtensions;

    public MediaImportService(
        ILogger<MediaImportService> logger,
        IMediaFileParsingService parser,
        IMediaRepository repository,
        IMediaService mediaService,
        IMediaFingerprintService fingerprints,
        IAnalyticsService analytics)
        : base(logger)
    {
        _parser = parser;
        _repository = repository;
        _mediaService = mediaService;
        _fingerprints = fingerprints;
        _analytics = analytics;
    }

    public Task StartAsync(IEnumerable<string> filePaths)
    {
        var paths = filePaths.ToList();
        if (paths.Count == 0)
            return Task.CompletedTask;

        lock (_startLock)
        {
            if (State == ImportState.Running)
                return Task.CompletedTask;

            _cts = new CancellationTokenSource();
            TotalCount = paths.Count;
            ImportedCount = 0;
            FailedCount = 0;
            CurrentFilePath = null;
            State = ImportState.Running;
            _lastNotifyMs = 0;
        }

        InvokeStateChanged();
        var cts = _cts!;
        var thread = new Thread(() => RunImportAsync(paths, cts.Token).GetAwaiter().GetResult())
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "MediaImport"
        };
        thread.Start();

        return Task.CompletedTask;
    }

    public void Cancel()
    {
        if (State != ImportState.Running)
            return;

        State = ImportState.Cancelling;
        _cts?.Cancel();
        InvokeStateChanged();
    }

    private void InvokeStateChangedThrottled()
    {
        var now = Environment.TickCount64;
        if (now - _lastNotifyMs < 250)
            return;

        _lastNotifyMs = now;
        InvokeStateChanged();
    }

    private async Task ImportOneFileAsync(ImportCandidate candidate, CancellationToken ct)
    {
        try
        {
            var media = await _parser.LoadAndParseAsync(candidate.Path);

            media.FileSize = candidate.Size;
            media.SampledHash = candidate.SampledHash
                ?? await _fingerprints.ComputeSampledHashAsync(candidate.Path, ct);
            media.ContentHash = candidate.ContentHash;

            await _mediaService.CreateAsync(media);
            ImportedCount++;
            _analytics.RecordImportFilesProcessed(1, "imported");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            FailedCount++;
            _analytics.RecordImportFilesProcessed(1, "failed");
            Logger.LogWarning(ex, "Failed to import {FilePath}", candidate.Path);
        }
    }

    private async Task RunImportAsync(List<string> paths, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = _analytics.StartActivity(AnalyticActivities.ImportBatch);

        try
        {
            var toImport = await FilterNewPathsAsync(paths, activity, ct);
            await ImportBatchAsync(toImport, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unhandled error in import background task");
        }
        finally
        {
            _analytics.RecordImportDuration(sw.Elapsed.TotalMilliseconds);
            State = ImportState.Idle;
            CurrentFilePath = null;
            _cts?.Dispose();
            _cts = null;
            InvokeStateChanged();
        }
    }

    private async Task<List<ImportCandidate>> FilterNewPathsAsync(
        List<string> paths, IAnalyticsActivity activity, CancellationToken ct)
    {
        var existing = await _repository.GetExistingFilePathsAsync(paths);
        var unseen = paths.Where(p => !existing.Contains(p)).ToList();

        var toImport = await FilterKnownContentAsync(unseen, ct);
        var skippedCount = paths.Count - toImport.Count;

        if (skippedCount > 0)
            _analytics.RecordImportFilesProcessed(skippedCount, "skipped");

        activity.SetTag("total_files", paths.Count);
        TotalCount = toImport.Count;
        InvokeStateChanged();

        return toImport;
    }

    /// <summary>
    /// Drops files whose bytes are already in the library under a different path. Size is the
    /// prefilter, so a file matching no stored size is new without ever being opened; only a size
    /// collision pays for a sampled hash, and only a sampled match pays for the full hash that
    /// confirms it. That last step is not optional — a false positive silently loses a song, which
    /// is worse than the duplicate row it would have prevented.
    /// </summary>
    private async Task<List<ImportCandidate>> FilterKnownContentAsync(List<string> paths, CancellationToken ct)
    {
        await MeasureUnsizedLibraryRowsAsync(ct);

        var sizes = new Dictionary<string, long>();
        foreach (var path in paths)
        {
            var size = _fingerprints.TryGetSize(path);
            if (size is not null)
                sizes[path] = size.Value;
        }

        var buckets = new Dictionary<long, List<Fingerprint>>();
        foreach (var row in await _repository.GetByFileSizesAsync(sizes.Values))
        {
            BucketFor(buckets, row.FileSize!.Value).Add(
                new Fingerprint(row.FilePath, row) { Sampled = row.SampledHash, Full = row.ContentHash });
        }

        var accepted = new List<ImportCandidate>(paths.Count);
        var updatedRows = new HashSet<Media>();

        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();

            if (!sizes.TryGetValue(path, out var size))
            {
                // Unreadable right now. Let the import run and report the real failure.
                accepted.Add(new ImportCandidate(path, null, null, null));
                continue;
            }

            // The incoming file joins its own bucket once accepted, so two identical files inside
            // one selection dedup against each other and not just against the library.
            var bucket = BucketFor(buckets, size);
            var incoming = new Fingerprint(path, Row: null);

            if (await MatchesKnownContentAsync(incoming, bucket, updatedRows, ct))
                continue;

            bucket.Add(incoming);
            accepted.Add(new ImportCandidate(path, size, incoming.Sampled, incoming.Full));
        }

        await _repository.UpdateFingerprintsAsync(updatedRows);

        return accepted;
    }

    private async Task<bool> MatchesKnownContentAsync(
        Fingerprint incoming, List<Fingerprint> bucket, HashSet<Media> updatedRows, CancellationToken ct)
    {
        // Nothing to compare against, so leave the hash to the import itself. Same total work
        // either way — the point is that filtering a large fresh library does no file I/O at all,
        // and progress starts moving immediately instead of after a read of every selected file.
        if (bucket.Count == 0)
            return false;

        incoming.Sampled = await _fingerprints.ComputeSampledHashAsync(incoming.FilePath, ct);
        if (incoming.Sampled is null)
            return false;

        foreach (var known in bucket)
        {
            ct.ThrowIfCancellationRequested();

            if (known.Sampled is null && !await FillSampledAsync(known, updatedRows, ct))
                continue;

            if (known.Sampled != incoming.Sampled)
                continue;

            incoming.Full ??= await _fingerprints.ComputeFullHashAsync(incoming.FilePath, ct);
            if (incoming.Full is null)
                return false;

            if (known.Full is null && !await FillFullAsync(known, updatedRows, ct))
                continue;

            if (known.Full != incoming.Full)
                continue;

            Logger.LogInformation(
                "Skipping {FilePath}: identical to {ExistingFilePath} already in the library",
                incoming.FilePath, known.FilePath);
            return true;
        }

        return false;
    }

    private async Task<bool> FillSampledAsync(Fingerprint known, HashSet<Media> updatedRows, CancellationToken ct)
    {
        known.Sampled = await _fingerprints.ComputeSampledHashAsync(known.FilePath, ct);
        if (known.Sampled is null)
            return false;

        if (known.Row is not null)
        {
            known.Row.SampledHash = known.Sampled;
            updatedRows.Add(known.Row);
        }

        return true;
    }

    private async Task<bool> FillFullAsync(Fingerprint known, HashSet<Media> updatedRows, CancellationToken ct)
    {
        known.Full = await _fingerprints.ComputeFullHashAsync(known.FilePath, ct);
        if (known.Full is null)
            return false;

        if (known.Row is not null)
        {
            known.Row.ContentHash = known.Full;
            updatedRows.Add(known.Row);
        }

        return true;
    }

    /// <summary>
    /// Gives rows imported before content dedup a size to match on. Stat only — no file is read —
    /// so this stays cheap even on a large library, and a row whose file has gone is left alone
    /// rather than marked, so it recovers if the drive comes back.
    /// </summary>
    private async Task MeasureUnsizedLibraryRowsAsync(CancellationToken ct)
    {
        var unsized = await _repository.GetWithoutFileSizeAsync();
        if (unsized.Count == 0)
            return;

        var measured = new List<Media>(unsized.Count);
        foreach (var row in unsized)
        {
            ct.ThrowIfCancellationRequested();

            var size = _fingerprints.TryGetSize(row.FilePath);
            if (size is null)
                continue;

            row.FileSize = size;
            measured.Add(row);
        }

        await _repository.UpdateFingerprintsAsync(measured);
        Logger.LogInformation("Measured {Count} library file(s) that predate content dedup", measured.Count);
    }

    private static List<Fingerprint> BucketFor(Dictionary<long, List<Fingerprint>> buckets, long size)
    {
        if (!buckets.TryGetValue(size, out var bucket))
            buckets[size] = bucket = [];

        return bucket;
    }

    private async Task ImportBatchAsync(List<ImportCandidate> toImport, CancellationToken ct)
    {
        foreach (var candidate in toImport)
        {
            if (ct.IsCancellationRequested)
                break;

            CurrentFilePath = candidate.Path;
            InvokeStateChangedThrottled();

            await ImportOneFileAsync(candidate, ct);

            InvokeStateChangedThrottled();
        }
    }

    private sealed record ImportCandidate(string Path, long? Size, string? SampledHash, string? ContentHash);

    /// <summary>A file's hashes, filled in on demand. <see cref="Row"/> is null for a file being imported.</summary>
    private sealed record Fingerprint(string FilePath, Media? Row)
    {
        public string? Sampled { get; set; }
        public string? Full { get; set; }
    }

    private static class AnalyticActivities
    {
        public const string ImportBatch = "media.import.batch";
    }
}
