using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IMediaService : IRepositoryService<Media>
{
    /// <summary>Listing and search reaching past karaoke; the plain overloads answer songs alone.</summary>
    Task<PaginatedResult<Media>> ReadAllAsync(int pageNumber, int pageSize, SortDescriptor? sort, MediaSearchOptions? options);

    Task<PaginatedResult<Media>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort, MediaSearchOptions? options);

    /// <summary>Every row of these types, unpaged and title-ordered, which is what a picker needs.</summary>
    Task<IReadOnlyList<Media>> ReadAllByTypesAsync(params MediaType[] types);
}
