using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Common.Media;
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
            Source = request.Source,
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
            Source = request.Source,
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

    public Task ReportDownloadProgressAsync(Guid mediaId, long bytesReceived, long? totalBytes)
    {
        _downloadsService.ReportProgress(mediaId, bytesReceived, totalBytes);
        return Task.CompletedTask;
    }

    // A settled row (Ready/Broken) has nothing left to cancel; an in-flight one reuses its
    // registered source rather than handing out a second, unreachable one for the same download.
    private CancellationToken TokenFor(Media media, MediaImportRequest request) => media.Status.IsAcquiring()
        ? _downloadsService.TokenForInFlight(media.Id, request.Title, request.Artist, request.Source)
        : CancellationToken.None;

    public async Task BeginProcessingAsync(Guid mediaId)
    {
        var media = await _mediaService.ReadAsync(mediaId);

        // The download entry is deliberately untouched: processing is the download's second phase,
        // so the entry stays Downloading until one of the settles resolves it.
        if (media is null || media.Status != MediaStatus.Downloading)
            return;

        media.Status = MediaStatus.Processing;

        // The row and its download entry move together, which is this service's whole job — the
        // entry stays Downloading and carries the phase, so the page stops reading a render's
        // progress as a download's.
        _downloadsService.ReportPhase(mediaId, DownloadPhase.Processing);

        await _mediaService.UpdateAsync(media);
    }

    public Task CompleteImportAsync(Guid mediaId) => SettleAsync(mediaId, MediaStatus.Ready, DownloadState.Completed);

    public Task FailImportAsync(Guid mediaId, string? reason = null)
        => SettleAsync(mediaId, MediaStatus.Broken, DownloadState.Failed, reason);

    public async Task DiscardImportAsync(Guid mediaId)
    {
        // A no-op if the host already cancelled it from the Downloads page — CancelAsync there
        // settles the entry itself, and this call has nothing left to find.
        _downloadsService.Settle(mediaId, DownloadState.Cancelled);

        var media = await _mediaService.ReadAsync(mediaId);

        // Ready and Broken rows are never deleted here — only one still in flight, in either phase.
        if (media is null || !media.Status.IsAcquiring())
            return;

        // The file is what the status used to stand in for, and the host can just look. A row
        // whose file outlived the cancel keeps the row: deleting it would leave the file on disk
        // with nothing pointing at it, for the folder scan to find later and import as Ready.
        if (File.Exists(media.FilePath))
        {
            Logger.LogWarning("Keeping media {MediaId} as Broken: {FilePath} outlived the cancel", mediaId, media.FilePath);

            media.Status = MediaStatus.Broken;
            await _mediaService.UpdateAsync(media);

            return;
        }

        await _mediaService.DeleteAsync(mediaId);
    }


    private async Task SettleAsync(Guid mediaId, MediaStatus status, DownloadState downloadState, string? reason = null)
    {
        _downloadsService.Settle(mediaId, downloadState, reason);

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
