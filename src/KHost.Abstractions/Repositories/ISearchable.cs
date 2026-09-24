using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

/// <summary>Paged text search over one kind of stored row, shared by the repositories and the
/// <see cref="Services.IRepositoryService{T}"/> services that wrap them.</summary>
/// <remarks>
/// <para>A plugin does not implement this. Search through the matching service (for example
/// <see cref="Services.IMediaService"/> or <see cref="Services.IUsersService"/>), which forwards to
/// the repository unchanged.</para>
/// <para>Paging is 1-based: a <c>pageNumber</c> below 1 reads page 1, a <c>pageSize</c> below 1
/// uses the default of 50, and anything above 1000 is capped at 1000. The returned
/// <see cref="PaginatedResult{T}"/> reports the page and size actually used, and
/// <see cref="PaginatedResult{T}.TotalCount"/> counts every match, not just this page.</para>
/// <para>A query matches anywhere within the searched text, without regard to case or accents, so
/// "bjork" finds "Björk". A null, empty or whitespace query matches every row. What is searched
/// depends on the repository; see each <c>I*Repository</c>. Safe to call from any thread.</para>
/// </remarks>
public interface ISearchable<T> where T : RepositoryModel
{
    /// <summary>Searches with repository-specific filters, in the repository's default order.</summary>
    /// <param name="query">Text to match; null, empty or whitespace matches every row.</param>
    /// <param name="pageNumber">1-based page to read; below 1 reads page 1.</param>
    /// <param name="pageSize">Rows per page; below 1 uses the default, and it is capped (see <see cref="ISearchable{T}"/>).</param>
    /// <param name="options">A filter object the repository recognises by type:
    /// <see cref="MediaSearchOptions"/> for media, <see cref="MediaPoolSearchOptions"/> for pools,
    /// <see cref="UserSearchOptions"/> for users. Any other type, or null, applies no extra filter,
    /// except on media, where it falls back to <see cref="MediaSearchOptions.Default"/> (karaoke only).</param>
    Task<PaginatedResult<T>> SearchAsync<TOptions>(string query, int pageNumber = 1, int pageSize = 50, TOptions? options = null) where TOptions : class;

    /// <summary>Searches in the repository's default order, with no extra filter.</summary>
    Task<PaginatedResult<T>> SearchAsync(string query, int pageNumber = 1, int pageSize = 50);

    /// <summary>Searches, ordered by <paramref name="sort"/>.</summary>
    /// <param name="query">Text to match; null, empty or whitespace matches every row.</param>
    /// <param name="pageNumber">1-based page to read; below 1 reads page 1.</param>
    /// <param name="pageSize">Rows per page; below 1 uses the default, and it is capped (see <see cref="ISearchable{T}"/>).</param>
    /// <param name="sort">A column key the repository lists (see each <c>I*Repository</c>). Null, or
    /// a key the repository does not know, falls back to its default order rather than throwing.</param>
    Task<PaginatedResult<T>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort);
}
