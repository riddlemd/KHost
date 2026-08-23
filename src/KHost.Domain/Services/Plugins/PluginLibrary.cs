using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Models;
using KHost.Plugins.Sdk.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services.Plugins;

/// <summary>
/// Host implementation behind every plugin's <see cref="IPluginContext.Library"/>.
/// </summary>
public class PluginLibrary : BaseService, IPluginLibrary
{
    private readonly IMediaRepository _repository;
    private readonly IMediaService _mediaService;
    private readonly ISingerQueueService _singerQueueService;
    private readonly IPerformanceService _performanceService;
    private readonly IOptionsMonitor<ServiceOptions> _options;
    private readonly PluginImportCancellations _cancellations;

    public PluginLibrary(
        ILogger<PluginLibrary> logger,
        IMediaRepository repository,
        IMediaService mediaService,
        ISingerQueueService singerQueueService,
        IPerformanceService performanceService,
        IOptionsMonitor<ServiceOptions> options,
        PluginImportCancellations cancellations)
        : base(logger)
    {
        _repository = repository;
        _mediaService = mediaService;
        _singerQueueService = singerQueueService;
        _performanceService = performanceService;
        _options = options;
        _cancellations = cancellations;
    }

    // Re-read on every access (not cached at construction) so a settings-page edit applies
    // without a restart, same as any other IOptionsMonitor-backed value.
    public string MediaDirectory
    {
        get
        {
            var configured = _options.CurrentValue.MediaDirectory;
            return string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "karaoke")
                : configured.Trim();
        }
    }

    public async Task<Guid> ImportAsync(MediaImportRequest request)
    {
        var existing = await _repository.FindByFilePathAsync(request.FilePath);
        if (existing is not null)
            return existing.Id;

        var created = await _mediaService.CreateAsync(new Media
        {
            FilePath = request.FilePath,
            Title = request.Title,
            Artist = request.Artist,
            Duration = request.Duration,
            Notes = request.Notes,
            Format = Path.GetExtension(request.FilePath).TrimStart('.').ToUpperInvariant(),
            Status = MediaStatus.Ready,
            DateAdded = DateTime.UtcNow,
        });

        return created.Id;
    }

    public async Task<ImportTicket> BeginImportAsync(MediaImportRequest request)
    {
        var existing = await _repository.FindByFilePathAsync(request.FilePath);
        if (existing is not null)
            return new ImportTicket { MediaId = existing.Id, Cancellation = TokenFor(existing) };

        var created = await _mediaService.CreateAsync(new Media
        {
            FilePath = request.FilePath,
            Title = request.Title,
            Artist = request.Artist,
            Duration = request.Duration,
            Notes = request.Notes,
            Format = Path.GetExtension(request.FilePath).TrimStart('.').ToUpperInvariant(),
            Status = MediaStatus.Downloading,
            DateAdded = DateTime.UtcNow,
        });

        var token = _cancellations.Register(created.Id);

        return new ImportTicket { MediaId = created.Id, Cancellation = token };
    }

    // A settled row (Ready/Broken) has nothing left to cancel; an in-flight one reuses its
    // registered source rather than handing out a second, unreachable one for the same download.
    private CancellationToken TokenFor(Media media) => media.Status == MediaStatus.Downloading
        ? _cancellations.TokenForInFlight(media.Id)
        : CancellationToken.None;

    public Task CompleteImportAsync(Guid mediaId) => SettleAsync(mediaId, MediaStatus.Ready);

    public Task FailImportAsync(Guid mediaId) => SettleAsync(mediaId, MediaStatus.Broken);

    public async Task DiscardImportAsync(Guid mediaId)
    {
        _cancellations.Remove(mediaId);

        var media = await _mediaService.ReadAsync(mediaId);

        // Ready and Broken rows are never deleted here — only a still-Downloading row can be,
        // which is the caller's proof (per the SDK contract) that no file survived the cancel.
        if (media is null || media.Status != MediaStatus.Downloading)
            return;

        await _mediaService.DeleteAsync(mediaId);
    }

    public async Task EnqueueAsync(Guid mediaId)
    {
        if (_singerQueueService.SelectedUserId is not { } singerId)
        {
            Logger.LogWarning("Cannot enqueue: no singer selected");
            return;
        }

        await _performanceService.CreateAndEnqueueAsync(new()
        {
            MediaId = mediaId,
            SingerId = singerId,
            CreatedDate = DateTime.UtcNow
        });

        Logger.LogInformation("Enqueued media {MediaId} for singer {SingerId}", mediaId, singerId);
    }

    private async Task SettleAsync(Guid mediaId, MediaStatus status)
    {
        _cancellations.Remove(mediaId);

        var media = await _mediaService.ReadAsync(mediaId);
        if (media is null)
        {
            Logger.LogWarning("Cannot set media {MediaId} to {Status}: no such row", mediaId, status);
            return;
        }

        media.Status = status;

        // BaseRepositoryService.UpdateAsync raises StateChanged itself.
        await _mediaService.UpdateAsync(media);
    }

    public sealed class ServiceOptions
    {
        public const string SectionName = "Plugins";

        /// <summary>Blank/null means "use the user-profile default" — resolved in <see cref="MediaDirectory"/>.</summary>
        public string? MediaDirectory { get; set; }
    }
}
