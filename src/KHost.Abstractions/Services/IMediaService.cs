using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IMediaService : IRepositoryService<Media>
{
    /// <summary>
    /// Listing and search that can reach past karaoke. The inherited overloads answer with songs
    /// alone; break music and ads are only visible to a caller that asks for them by name.
    /// </summary>
    Task<PaginatedResult<Media>> ReadAllAsync(int pageNumber, int pageSize, SortDescriptor? sort, MediaSearchOptions? options);

    Task<PaginatedResult<Media>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort, MediaSearchOptions? options);
}
