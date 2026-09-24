using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

/// <summary>Create, read, update and delete for one kind of row in the host's library.</summary>
/// <remarks>
/// <para>The host implements this. A plugin does not, and should normally not call it either: every
/// repository has a matching <see cref="Services.IRepositoryService{T}"/> (for example
/// <see cref="IUsersRepository"/> and <see cref="Services.IUsersService"/>). The service announces
/// a change message on the <see cref="Messaging.IMessageBroker"/> after every write so open pages
/// redraw, and holds the rules this layer does not (which rows may be deleted, which fields must be
/// set). A write made here is persisted but announced to nobody.</para>
/// <para>A singleton, safe to call from any thread. A returned entity is a copy: changing it
/// changes nothing stored until it is passed to <see cref="UpdateAsync"/>. Paging follows
/// <see cref="ISearchable{T}"/>.</para>
/// </remarks>
public interface IRepository<T> : ISearchable<T> where T : RepositoryModel
{
    /// <summary>Inserts the row and returns it with its generated values filled in.</summary>
    /// <exception cref="Exceptions.KHostException">A conflict the host can explain, such as a
    /// singer name already taken.</exception>
    Task<T> CreateAsync(T entity);

    /// <summary>The row with this id, or null if there is none.</summary>
    Task<T?> ReadAsync(Guid id);

    /// <summary>Overwrites the stored row with every property of <paramref name="entity"/>.</summary>
    /// <remarks>Whole-row: a property left at its default is saved as that default.</remarks>
    /// <exception cref="Exceptions.KHostException">A conflict the host can explain, as for
    /// <see cref="CreateAsync"/>.</exception>
    Task UpdateAsync(T entity);

    /// <summary>Deletes the row; false when no row had this id.</summary>
    Task<bool> DeleteAsync(Guid id);

    /// <summary>One page of every row, in the repository's default order.</summary>
    Task<PaginatedResult<T>> ReadAllAsync(int pageNumber = 1, int pageSize = 0);

    /// <summary>One page of every row, ordered by <paramref name="sort"/>.</summary>
    /// <param name="pageNumber">1-based page to read; below 1 reads page 1.</param>
    /// <param name="pageSize">Rows per page; below 1 uses the default, and it is capped (see <see cref="ISearchable{T}"/>).</param>
    /// <param name="sort">A column key the repository lists; null or unknown uses the default order.</param>
    Task<PaginatedResult<T>> ReadAllAsync(int pageNumber, int pageSize, SortDescriptor? sort);

    /// <summary>Whether the table holds any row at all.</summary>
    Task<bool> HasAnyAsync();
}
