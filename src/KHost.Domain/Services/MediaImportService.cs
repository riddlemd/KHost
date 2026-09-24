using KHost.Abstractions.Models;
using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using Microsoft.Extensions.Logging;
using KHost.Common.Media;

namespace KHost.Domain.Services;

public class MediaImportService : BaseService, IMediaImportService
{
    // Everything the host can already play, not just songs: a venue's break music, its ad clips
    // and the card it puts up between singers are all ordinary library rows too.
    private static readonly string[] _supportedExtensions =
    [
        MediaFormats.KaraokeGraphicsExtension,
        .. MediaFormats.VideoExtensions,
        .. MediaFormats.AudioExtensions,
        .. MediaFormats.ImageExtensions,
    ];

    private readonly IMediaFileParsingService _parser;
    private readonly IMessageBroker _broker;
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
    // The built-in formats plus whatever loaded plugins declare: asserting the host can already play it,
    // so the scanner stops skipping their output. Computed once: plugins are fixed until restart.
    public IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>Karaoke or ad clip: the two share formats, so the host names the folder.</summary>
    public bool VideoIsKaraoke { get; set; } = true;

    /// <inheritdoc />
    /// <remarks>Case-insensitive: a path typed by a host and one read from a listing differ.</remarks>
    public IDictionary<string, MediaType> TypeOverrides { get; } =
        new Dictionary<string, MediaType>(StringComparer.OrdinalIgnoreCase);

    public MediaImportService(
        ILogger<MediaImportService> logger,
        IMediaFileParsingService parser,
        IMediaRepository repository,
        IMediaService mediaService,
        IMediaFingerprintService fingerprints,
        IAnalyticsService analytics,
        IPluginRegistry plugins,
        IMessageBroker broker)
        : base(logger)
    {
        _broker = broker;
        _parser = parser;
        _repository = repository;
        _mediaService = mediaService;
        _fingerprints = fingerprints;
        _analytics = analytics;

        SupportedExtensions = NormalizeExtensions(_supportedExtensions.Concat(
            plugins.Plugins
                // Loaded only: an unloaded plugin's format has no owner to have produced it.
                .Where(plugin => plugin.Status == PluginStatus.Loaded)
                .SelectMany(plugin => plugin.Manifest?.ImportFormats ?? [])));
    }

    /// <summary>Leading-dot, lowercase, de-duped: a plugin may hand back "khv", "*.KHV", ".Khv".</summary>
    private static IReadOnlyList<string> NormalizeExtensions(IEnumerable<string> extensions) =>
    [
        .. extensions
            .Select(extension => "." + extension.Trim().TrimStart('*').TrimStart('.').ToLowerInvariant())
            .Where(extension => extension.Length > 1)
            .Distinct()
    ];

    public Task StartAsync(IEnumerable<string> filePaths)
    {
        var paths = WithoutPairedAudio(filePaths).ToList();
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

        _broker.Announce(new MediaImportChanged());
        var cts = _cts!;
        _ = Task.Run(() => RunImportAsync(paths, cts));

        return Task.CompletedTask;
    }

    /// <summary>Drops the audio half of a karaoke pair, keeping the .cdg as the row.</summary>
    /// <remarks>A .cdg proves the pair is karaoke; an .mp3 alone proves nothing, which is why the
    /// graphics file is the one that becomes the row. Imported on its own the .mp3 is a second row
    /// for the same song that plays the backing track against a blank screen.</remarks>
    internal static IEnumerable<string> WithoutPairedAudio(IEnumerable<string> filePaths)
        => filePaths.Where(path =>
            !MediaFormats.AudioExtensions.Contains(Path.GetExtension(path).ToLowerInvariant())
            || MediaFormats.FindKaraokeGraphics(path) is null);

    public void Cancel()
    {
        if (State != ImportState.Running)
            return;

        State = ImportState.Cancelling;
        _cts?.Cancel();
        _broker.Announce(new MediaImportChanged());
    }

    private void AnnounceThrottled()
    {
        var now = Environment.TickCount64;
        if (now - _lastNotifyMs < 250)
            return;

        _lastNotifyMs = now;
        _broker.Announce(new MediaImportChanged());
    }

    private async Task ImportOneFileAsync(ImportCandidate candidate, CancellationToken ct)
    {
        try
        {
            // Asked rather than assumed: nothing in a picture-track file settles karaoke vs. ad clip, so
            // the host's own answer for this file beats anything worked out from its name.
            var type = TypeOverrides.TryGetValue(candidate.Path, out var chosen)
                ? chosen
                : MediaFormats.TypeForFile(candidate.Path, VideoIsKaraoke);

            // Half a song is not a row. A .cdg carries the words and no sound, so without the audio
            // beside it there is nothing to play — and imported anyway it reached the room as
            // silence, which is the one symptom that never points at its own cause.
            if (MediaFormats.IsGraphicsOnlyKaraoke(candidate.Path)
                && MediaFormats.FindKaraokeAudio(candidate.Path) is null)
            {
                FailedCount++;
                _analytics.RecordImportFilesProcessed(1, "failed");
                Logger.LogWarning(
                    "Skipping {FilePath}: no audio file beside it, so the pair is incomplete",
                    candidate.Path);
                return;
            }

            var media = await _parser.LoadAndParseAsync(candidate.Path, type);

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

    private async Task RunImportAsync(List<string> paths, CancellationTokenSource cts)
    {
        var ct = cts.Token;
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
            CurrentFilePath = null;

            // Disposing the field (rather than this captured instance) risks tearing down a fresh
            // CTS a concurrent StartAsync already installed; nulling it unconditionally would then
            // strand that run with no token source at all. Idle is set last so a StartAsync racing
            // this teardown still sees Running and backs off instead of reusing a torn-down field.
            cts.Dispose();
            if (ReferenceEquals(_cts, cts))
                _cts = null;
            State = ImportState.Idle;

            _broker.Announce(new MediaImportChanged());
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
        _broker.Announce(new MediaImportChanged());

        return toImport;
    }

    /// <summary>Drops files already in the library; a size collision pays for a sampled hash.</summary>
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
        // Nothing to compare against, so leave the hash to the import itself. Same total work either way,
        // but filtering a large fresh library does no file I/O, so progress starts moving immediately.
        if (bucket.Count == 0)
            return false;

        incoming.Sampled = await _fingerprints.ComputeSampledHashAsync(incoming.FilePath, ct);
        if (incoming.Sampled is null)
            return false;

        foreach (var known in bucket)
        {
            ct.ThrowIfCancellationRequested();

            if (known.Sampled is null && !await FillAsync(
                known, updatedRows, _fingerprints.ComputeSampledHashAsync,
                (fp, hash) => fp.Sampled = hash, (row, hash) => row.SampledHash = hash, ct))
                continue;

            if (known.Sampled != incoming.Sampled)
                continue;

            incoming.Full ??= await _fingerprints.ComputeFullHashAsync(incoming.FilePath, ct);
            if (incoming.Full is null)
                return false;

            if (known.Full is null && !await FillAsync(
                known, updatedRows, _fingerprints.ComputeFullHashAsync,
                (fp, hash) => fp.Full = hash, (row, hash) => row.ContentHash = hash, ct))
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

    /// <summary>Computes whichever tier of hash is missing and stamps it on both the in-memory
    /// fingerprint and the library row it came from, one caller for the sampled and full tiers.</summary>
    private static async Task<bool> FillAsync(
        Fingerprint known,
        HashSet<Media> updatedRows,
        Func<string, CancellationToken, Task<string?>> hasher,
        Action<Fingerprint, string> setOnFingerprint,
        Action<Media, string> setOnRow,
        CancellationToken ct)
    {
        var hash = await hasher(known.FilePath, ct);
        if (hash is null)
            return false;

        setOnFingerprint(known, hash);

        if (known.Row is not null)
        {
            setOnRow(known.Row, hash);
            updatedRows.Add(known.Row);
        }

        return true;
    }

    /// <summary>Gives pre-dedup rows a size to match on; a missing file is left alone, not marked.</summary>
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
            AnnounceThrottled();

            await ImportOneFileAsync(candidate, ct);

            AnnounceThrottled();
        }
    }

    private sealed record ImportCandidate(string Path, long? Size, string? SampledHash, string? ContentHash);

    /// <summary>A files hashes, filled in on demand. Row is null for a file being imported.</summary>
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
