using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.Domain.Services;

public class MediaService : BaseRepositoryService<Media, IMediaRepository>, IMediaService
{

    // Resolved on use, never in the constructor: PerformanceService takes IMediaService, so asking
    // for it here would close a ring and hang the app before it logs a line. Nothing deletes media
    // until long after the graph is up, so the lookup is free by then.
    private readonly IServiceProvider _services;
    private IPerformanceService? _performanceService;

    private IPerformanceService Performances =>
        _performanceService ??= _services.GetRequiredService<IPerformanceService>();

    public MediaService(
        ILogger<MediaService> logger,
        IMediaRepository repository,
        IMessageBroker broker,
        IServiceProvider services)
        : base(logger, repository, broker, new MediaLibraryChanged())
    {
        _services = services;
    }

    /// <summary>
    /// Takes the song out of every queue it is waiting in before deleting it. A queued performance
    /// carries a media id and nothing else, and there are deliberately no foreign keys here — so
    /// without this the row survived its song, sat in a singer's queue looking ordinary, and
    /// failed only when somebody tried to play it.
    /// </summary>
    /// <remarks>
    /// Queued rows only. A performance already sung keeps its media id whether or not the file is
    /// still in the library: that is the record this schema drops foreign keys to protect, and it
    /// is exactly the thing a cleanup must not take with it.
    ///
    /// Before the media goes, not after: dequeuing reads the media to see whether a download is
    /// still running, and there would be nothing left to read.
    /// </remarks>
    public override async Task<bool> DeleteAsync(Guid id)
    {
        try
        {
            var queued = await Performances.ReadByMediaIdAsync(id, pageSize: 0, filter: PerformanceFilter.Queued);

            foreach (var performance in queued.Items)
                await Performances.DeleteAsync(performance.Id);

            if (queued.Items.Count > 0)
                Logger.LogInformation(
                    "Took media {MediaId} out of {Count} queue entry(s) before deleting it", id, queued.Items.Count);
        }
        catch (Exception ex)
        {
            // A tidy-up that fails must not strand the delete the host asked for; the row would
            // then need deleting twice and look like it had ignored them.
            Logger.LogWarning(ex, "Could not clear queued performances for media {MediaId}", id);
        }

        return await base.DeleteAsync(id);
    }

    public Task<PaginatedResult<Media>> ReadAllAsync(int pageNumber, int pageSize, SortDescriptor? sort, MediaSearchOptions? options)
        => Repository.ReadAllAsync(pageNumber, pageSize, sort, options);

    public Task<PaginatedResult<Media>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort, MediaSearchOptions? options)
        => Repository.SearchAsync(query, pageNumber, pageSize, sort, options);

    public Task<IReadOnlyList<Media>> ReadAllByTypesAsync(params MediaType[] types)
        => Repository.ReadAllByTypesAsync(types);
}
