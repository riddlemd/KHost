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

    private CancellationTokenSource? _cts;
    private readonly Lock _startLock = new();

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
        IMediaService mediaService)
        : base(logger)
    {
        _parser = parser;
        _repository = repository;
        _mediaService = mediaService;
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
        }

        InvokeStateChanged();
        _ = Task.Run(() => RunImportAsync(paths, _cts!.Token));

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

    private async Task RunImportAsync(List<string> paths, CancellationToken ct)
    {
        try
        {
            var existing = await _repository.GetExistingFilePathsAsync(paths);
            var toImport = paths.Where(p => !existing.Contains(p)).ToList();

            TotalCount = toImport.Count;
            InvokeStateChanged();

            foreach (var path in toImport)
            {
                if (ct.IsCancellationRequested)
                    break;

                CurrentFilePath = path;
                InvokeStateChanged();

                try
                {
                    var media = await Task.Run(() => _parser.LoadAndParse(path), ct);
                    await _mediaService.CreateAsync(media);
                    ImportedCount++;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    FailedCount++;
                    Logger.LogWarning(ex, "Failed to import {FilePath}", path);
                }

                InvokeStateChanged();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unhandled error in import background task");
        }
        finally
        {
            State = ImportState.Idle;
            CurrentFilePath = null;
            _cts?.Dispose();
            _cts = null;
            InvokeStateChanged();
        }
    }
}
