using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services;

public class MediaAcquisitionService : BaseService, IMediaAcquisitionService
{
    private readonly IMediaRepository _repository;
    private readonly IMediaService _mediaService;
    private readonly IOptionsMonitor<ServiceOptions> _options;
    private readonly IDownloadsService _downloadsService;

    public MediaAcquisitionService(
        ILogger<MediaAcquisitionService> logger,
        IMediaRepository repository,
        IMediaService mediaService,
        IOptionsMonitor<ServiceOptions> options,
        IDownloadsService downloadsService,
        IMessageBroker broker)
        : base(logger)
    {
        _repository = repository;
        _mediaService = mediaService;
        _options = options;
        _downloadsService = downloadsService;
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
            return new ImportTicket { MediaId = existing.Id, Cancellation = TokenFor(existing, request) };

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

        var token = _downloadsService.Register(created.Id, request.Title, request.Artist, request.Source);

        return new ImportTicket { MediaId = created.Id, Cancellation = token };
    }

    public Task ReportDownloadProgressAsync(Guid mediaId, double fraction)
    {
        _downloadsService.ReportProgress(mediaId, fraction);
        return Task.CompletedTask;
    }

    // A settled row (Ready/Broken) has nothing left to cancel; an in-flight one reuses its
    // registered source rather than handing out a second, unreachable one for the same download.
    private CancellationToken TokenFor(Media media, MediaImportRequest request) => media.Status == MediaStatus.Downloading
        ? _downloadsService.TokenForInFlight(media.Id, request.Title, request.Artist, request.Source)
        : CancellationToken.None;

    public Task CompleteImportAsync(Guid mediaId) => SettleAsync(mediaId, MediaStatus.Ready, DownloadState.Completed);

    public Task FailImportAsync(Guid mediaId) => SettleAsync(mediaId, MediaStatus.Broken, DownloadState.Failed);

    public async Task DiscardImportAsync(Guid mediaId)
    {
        // A no-op if the host already cancelled it from the Downloads page — CancelAsync there
        // settles the entry itself, and this call has nothing left to find.
        _downloadsService.Settle(mediaId, DownloadState.Cancelled);

        var media = await _mediaService.ReadAsync(mediaId);

        // Ready and Broken rows are never deleted here — only a still-Downloading row can be,
        // which is the caller's proof (per the import contract) that no file survived the cancel.
        if (media is null || media.Status != MediaStatus.Downloading)
            return;

        await _mediaService.DeleteAsync(mediaId);
    }


    private async Task SettleAsync(Guid mediaId, MediaStatus status, DownloadState downloadState)
    {
        _downloadsService.Settle(mediaId, downloadState);

        var media = await _mediaService.ReadAsync(mediaId);
        if (media is null)
        {
            Logger.LogWarning("Cannot set media {MediaId} to {Status}: no such row", mediaId, status);
            return;
        }

        media.Status = status;

        // BaseRepositoryService.UpdateAsync announces the change itself.
        await _mediaService.UpdateAsync(media);
    }

    public sealed class ServiceOptions
    {
        public const string SectionName = "Plugins";

        /// <summary>Blank/null means "use the user-profile default" — resolved in <see cref="MediaDirectory"/>.</summary>
        public string? MediaDirectory { get; set; }
    }
}
