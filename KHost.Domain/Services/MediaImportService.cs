using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;
using System.Runtime.Intrinsics.X86;

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
        IAnalyticsService analytics)
        : base(logger)
    {
        _parser = parser;
        _repository = repository;
        _mediaService = mediaService;
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

    private async Task RunImportAsync(List<string> paths, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = _analytics.StartActivity(AnalyticActivities.ImportBatch);

        try
        {
            var existing = await _repository.GetExistingFilePathsAsync(paths);
            var toImport = paths.Where(p => !existing.Contains(p)).ToList();
            var skippedCount = paths.Count - toImport.Count;

            if (skippedCount > 0)
                _analytics.RecordImportFilesProcessed(skippedCount, "skipped");

            activity.SetTag("total_files", paths.Count);
            TotalCount = toImport.Count;
            InvokeStateChanged();

            foreach (var path in toImport)
            {
                if (ct.IsCancellationRequested)
                    break;

                CurrentFilePath = path;
                InvokeStateChangedThrottled();

                try
                {
                    var media = await _parser.LoadAndParse(path);
                    await _mediaService.CreateAsync(media);
                    ImportedCount++;
                    _analytics.RecordImportFilesProcessed(1, "imported");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    FailedCount++;
                    _analytics.RecordImportFilesProcessed(1, "failed");
                    Logger.LogWarning(ex, "Failed to import {FilePath}", path);
                }

                InvokeStateChangedThrottled();
            }
        }
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

    private static class AnalyticActivities
    {
        public const string ImportBatch = "media.import.batch";
    }
}
