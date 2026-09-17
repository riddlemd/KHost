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

    // Resolved on use, never in the constructor: PerformanceService takes IMediaService, so asking here
    // would close a ring and hang the app before it logs a line; nothing deletes media until long after.
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

    /// <summary>Takes the song out of every queue it waits in; no FK stops it outliving the song.</summary>
    /// <remarks>Queued rows only; a sung performance keeps its media id.</remarks>
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
