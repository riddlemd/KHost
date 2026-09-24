using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>The media library: every song, break-music track, ad clip and still the host can play.
/// </summary>
/// <remarks>A plugin may take it to read or edit library rows. To add a file it has downloaded, use
/// <see cref="IMediaAcquisitionService"/> instead, which keeps the row and its download entry
/// together. Deleting a row also takes it out of every queue it waits in; performances already sung
/// keep their reference to it. A host singleton, callable from any thread. Every create, update and
/// delete announces <see cref="KHost.Abstractions.Messaging.Messages.MediaLibraryChanged"/>.</remarks>
public interface IMediaService : IRepositoryService<Media>
{
    /// <summary>Listing and search reaching past karaoke; the plain overloads answer songs alone.</summary>
    /// <param name="pageNumber">1-based page to read; below 1 reads page 1.</param>
    /// <param name="pageSize">Rows per page; below 1 uses the default page size.</param>
    /// <param name="sort">A sort key <see cref="Repositories.IMediaRepository"/> lists; null or unknown
    /// uses the default order.</param>
    /// <param name="options">Which types and statuses to include; null answers songs alone, as the
    /// plain overloads do.</param>
    Task<PaginatedResult<Media>> ReadAllAsync(int pageNumber, int pageSize, SortDescriptor? sort, MediaSearchOptions? options);

    /// <summary>Rows matching <paramref name="query"/>, filtered by <paramref name="options"/>.</summary>
    /// <inheritdoc cref="ReadAllAsync(int, int, SortDescriptor?, MediaSearchOptions?)" path="/param"/>
    Task<PaginatedResult<Media>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort, MediaSearchOptions? options);

    /// <summary>Every row of these types, unpaged and title-ordered, which is what a picker needs.</summary>
    Task<IReadOnlyList<Media>> ReadAllByTypesAsync(params MediaType[] types);
}
