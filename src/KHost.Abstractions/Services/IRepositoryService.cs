using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;

namespace KHost.Abstractions.Services;

/// <summary>The create, read, update and delete every library-backed service shares.</summary>
/// <remarks>The persisted services — media, pools, performances, users, groups, venues, tips —
/// extend this, and each announces its own change message on every create, update and successful
/// delete; see the service for which. Paging is 1-based throughout: a page number below 1 reads the
/// first page, and a page size of zero or less takes the host's default, with an upper limit the
/// host enforces. Implementations are host singletons, callable from any thread.</remarks>
public interface IRepositoryService<T> : ISearchable<T>
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

    /// <summary>Whether any row exists at all.</summary>
    Task<bool> HasAnyAsync();
}
