using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Common.Media;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.MediaLifetime;

public sealed class MediaLifetimeService : IMediaLifetimeService
{
    private readonly ILogger<MediaLifetimeService> _logger;
    private readonly IMediaRepository _repository;
    private readonly IMediaService _mediaService;
    private readonly MediaAcquisitionService _acquisition;
    private readonly IEnumerable<IMediaRefetcher> _refetchers;

    public MediaLifetimeService(
        ILogger<MediaLifetimeService> logger,
        IMediaRepository repository,
        IMediaService mediaService,
        MediaAcquisitionService acquisition,
        IEnumerable<IMediaRefetcher> refetchers)
    {
        _logger = logger;
        _repository = repository;
        _mediaService = mediaService;
        _acquisition = acquisition;
        _refetchers = refetchers;
    }

    public async Task<bool> RefetchAsync(Media media)
    {
        if (media.Status != MediaStatus.NotDownloaded || RefetcherFor(media) is not { } refetcher)
            return false;

        var ticket = await _acquisition.BeginRefetchAsync(media);

        _logger.LogInformation("Fetching the file of media {MediaId} again from {Source}", media.Id, media.Source);

        _ = Task.Run(async () =>
        {
            try
            {
                await refetcher.RefetchAsync(media, ticket);
            }
            catch (OperationCanceledException) when (ticket.Cancellation.IsCancellationRequested)
            {
                await _acquisition.DiscardImportAsync(media.Id);
            }
            catch (Exception ex)
            {
                // The provider broke its side of the ticket; settled here so the row does not sit Downloading.
                _logger.LogWarning(ex, "{Source} failed to fetch the file of media {MediaId} again", media.Source, media.Id);
                await _acquisition.FailImportAsync(media.Id);
            }
        });

        return true;
    }

    public async Task RemoveFilesOnCloseAsync(IReadOnlyCollection<Guid> queuedMediaIds)
    {
        var removed = 0;

        foreach (var media in await _repository.ReadWithFileLifetimeAsync())
        {
            // A row still arriving is the downloads' to settle; the startup sweep covers it after a close.
            if (media.Status.IsAcquiring() || media.Status == MediaStatus.NotDownloaded)
                continue;

            // A turn carried into the next session (a venue that keeps its queue) is played, never
            // re-queued, so nothing would fetch its file again.
            if (queuedMediaIds.Contains(media.Id))
                continue;

            if (await RemoveFileAsync(media))
                removed++;
        }

        _logger.LogInformation("Removed {Count} ephemeral or single-use file(s) on close", removed);
    }

    public async Task RemoveSingleUseFileIfDoneAsync(Guid mediaId, IReadOnlyCollection<Guid> queuedMediaIds)
    {
        if (queuedMediaIds.Contains(mediaId)) return;

        if (await _mediaService.ReadAsync(mediaId) is not { IsSingleUse: true } media
            || media.Status.IsAcquiring() || media.Status == MediaStatus.NotDownloaded)
            return;

        if (await RemoveFileAsync(media))
            _logger.LogInformation("Removed the file of single-use media {MediaId} after it was sung", mediaId);
    }

    /// <summary>Deletes the file and keeps the row as NotDownloaded; the host's own transition.</summary>
    /// <returns>False when the file could not be deleted, which leaves the row as it was.</returns>
    private async Task<bool> RemoveFileAsync(Media media)
    {
        try
        {
            if (File.Exists(media.FilePath))
                File.Delete(media.FilePath);
        }
        catch (Exception ex)
        {
            // A file held open (a screen still reading it) stays; the next pass tries it again.
            _logger.LogWarning(ex, "Could not delete {FilePath}; media {MediaId} keeps its file", media.FilePath, media.Id);
            return false;
        }

        media.Status = MediaStatus.NotDownloaded;
        await _mediaService.UpdateAsync(media);
        return true;
    }

    private IMediaRefetcher? RefetcherFor(Media media)
    {
        foreach (var refetcher in _refetchers)
        {
            try
            {
                if (refetcher.CanRefetch(media)) return refetcher;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A provider threw deciding whether it can fetch media {MediaId} again", media.Id);
            }
        }

        return null;
    }
}
