using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>The create, read, update and delete every library-backed service shares.</summary>
/// <remarks>The persisted services — media, pools, performances, users, groups, venues, tips —
/// extend this, and each announces its own change message on every create, update and successful
/// delete; see the service for which. Paging is 1-based throughout: a page number below 1 reads the
/// first page, and a page size of zero or less takes the host's default, with an upper limit the
/// host enforces; a returned <see cref="PaginatedResult{T}"/> reports the page and size actually
/// used, and its total counts every match, not just that page. Implementations are host
/// singletons, callable from any thread.
///
/// <para>Search matches a query anywhere within the searched text, without regard to case or
/// accents, so "bjork" finds "Björk"; a null, empty or whitespace query matches every row. Each
/// service says what it searches; one with a filter of its own takes it as a typed overload of
/// <c>SearchAsync</c>.</para></remarks>
public interface IRepositoryService<T>
    where T : RepositoryModel
{
    /// <summary>Saves a new row.</summary>
    /// <returns>The row as saved.</returns>
    Task<T> CreateAsync(T entity);

    /// <summary>The row with <paramref name="id"/>, or null when there is none.</summary>
    Task<T?> ReadAsync(Guid id);

    /// <summary>Saves changes to an existing row.</summary>
    Task UpdateAsync(T entity);

    /// <summary>Deletes the row with <paramref name="id"/>.</summary>
    /// <returns>False when there was no such row; nothing is announced then.</returns>
    Task<bool> DeleteAsync(Guid id);

    /// <summary>One page of every row, in the store's default order.</summary>
    Task<PaginatedResult<T>> ReadAllAsync(int pageNumber = 1, int pageSize = 0);

    /// <summary>One page of every row, ordered by <paramref name="sort"/>; null takes the default
    /// order.</summary>
    Task<PaginatedResult<T>> ReadAllAsync(int pageNumber, int pageSize, SortDescriptor? sort);

    /// <summary>Rows matching <paramref name="query"/>, in the service's default order.</summary>
    /// <param name="query">Text to match; null, empty or whitespace matches every row.</param>
    /// <param name="pageNumber">1-based page to read; below 1 reads page 1.</param>
    /// <param name="pageSize">Rows per page; below 1 uses the default page size.</param>
    Task<PaginatedResult<T>> SearchAsync(string query, int pageNumber = 1, int pageSize = 50);

    /// <summary>Rows matching <paramref name="query"/>, ordered by <paramref name="sort"/>.</summary>
    /// <param name="query">Text to match; null, empty or whitespace matches every row.</param>
    /// <param name="pageNumber">1-based page to read; below 1 reads page 1.</param>
    /// <param name="pageSize">Rows per page; below 1 uses the default page size.</param>
    /// <param name="sort">A column key the service lists. Null, or a key it does not know, takes the
    /// default order rather than throwing.</param>
    Task<PaginatedResult<T>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort);

    /// <summary>Whether any row exists at all.</summary>
    Task<bool> HasAnyAsync();
}
