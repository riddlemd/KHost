using KHost.Abstractions.Models;

namespace KHost.DataAccess.Repositories;

/// <summary>Paged text search over one kind of stored row, for a repository whose rows hold text.
/// </summary>
/// <remarks>
/// <para>Opt-in: a repository implements it only when it has text to match. Paging is 1-based: a
/// <c>pageNumber</c> below 1 reads page 1, a <c>pageSize</c> below 1 uses the default of 50, and
/// anything above 1000 is capped at 1000. The returned <see cref="PaginatedResult{T}"/> reports the
/// page and size actually used, and <see cref="PaginatedResult{T}.TotalCount"/> counts every match,
/// not just this page.</para>
/// <para>A query matches anywhere within the searched text, without regard to case or accents, so
/// "bjork" finds "Björk". A null, empty or whitespace query matches every row. Each repository says
/// what it searches. Safe to call from any thread.</para>
/// </remarks>
public interface ISearchable<T> where T : RepositoryModel
{
    /// <summary>Searches in the repository's default order, with no extra filter.</summary>
    Task<PaginatedResult<T>> SearchAsync(string query, int pageNumber = 1, int pageSize = 50);

    /// <summary>Searches, ordered by <paramref name="sort"/>.</summary>
    /// <param name="query">Text to match; null, empty or whitespace matches every row.</param>
    /// <param name="pageNumber">1-based page to read; below 1 reads page 1.</param>
    /// <param name="pageSize">Rows per page; below 1 uses the default, and it is capped (see <see cref="ISearchable{T}"/>).</param>
    /// <param name="sort">A column key the repository lists. Null, or a key the repository does not
    /// know, falls back to its default order rather than throwing.</param>
    Task<PaginatedResult<T>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort);
}

/// <summary>Search that also takes a filter of one type, for a repository that has one.</summary>
/// <remarks>The plain overloads of <see cref="ISearchable{T}"/> behave as this one with null
/// options; each repository says what null means for it.</remarks>
public interface ISearchable<T, in TOptions> : ISearchable<T>
    where T : RepositoryModel
    where TOptions : class
{
    /// <summary>Searches with <paramref name="options"/> applied, ordered by <paramref name="sort"/>.</summary>
    /// <param name="query">Text to match; null, empty or whitespace matches every row.</param>
    /// <param name="pageNumber">1-based page to read; below 1 reads page 1.</param>
    /// <param name="pageSize">Rows per page; below 1 uses the default, and it is capped (see <see cref="ISearchable{T}"/>).</param>
    /// <param name="sort">As for <see cref="ISearchable{T}.SearchAsync(string, int, int, SortDescriptor?)"/>.</param>
    /// <param name="options">The filter; null means what the repository documents for it.</param>
    Task<PaginatedResult<T>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort, TOptions? options);
}
