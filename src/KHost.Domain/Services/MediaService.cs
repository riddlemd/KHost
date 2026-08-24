using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

public class MediaService : BaseRepositoryService<Media, IMediaRepository>, IMediaService
{
    protected override object? StateChangedMessage => new MediaLibraryChanged();

    public MediaService(ILogger<MediaService> logger, IMediaRepository repository, IMessageBroker broker)
        : base(logger, repository, broker)
    {
    }

    public Task<PaginatedResult<Media>> ReadAllAsync(int pageNumber, int pageSize, SortDescriptor? sort, MediaSearchOptions? options)
        => Repository.ReadAllAsync(pageNumber, pageSize, sort, options);

    public Task<PaginatedResult<Media>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort, MediaSearchOptions? options)
        => Repository.SearchAsync(query, pageNumber, pageSize, sort, options);

    public Task<IReadOnlyList<Media>> ReadAllByTypesAsync(params MediaType[] types)
        => Repository.ReadAllByTypesAsync(types);
}
