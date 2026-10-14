using KHost.Abstractions.Models;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KHost.DataAccess.Repositories;

/// <summary>A repository whose rows hold text to search, with no filter of its own.</summary>
internal abstract class BaseSearchableRepository<T> : BaseRepository<T>, ISearchable<T> where T : RepositoryModel
{
    protected BaseSearchableRepository(IDbContextFactory<DefaultContext> contextFactory, ILogger logger)
        : base(contextFactory, logger)
    {
    }

    public virtual Task<PaginatedResult<T>> SearchAsync(string query, int pageNumber = 1, int pageSize = 50)
        => SearchAsync(query, pageNumber, pageSize, sort: null);

    public virtual Task<PaginatedResult<T>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort)
        => SearchFilteredAsync(query, pageNumber, pageSize, sort, q => ApplySearchFilters(q, query));

    /// <summary>Narrows <paramref name="queryable"/> to rows matching <paramref name="query"/>.</summary>
    protected abstract IQueryable<T> ApplySearchFilters(IQueryable<T> queryable, string query);
}

/// <summary>A repository whose search also takes a filter of type <typeparamref name="TOptions"/>.</summary>
internal abstract class BaseSearchableRepository<T, TOptions> : BaseRepository<T>, ISearchable<T, TOptions>
    where T : RepositoryModel
    where TOptions : class
{
    protected BaseSearchableRepository(IDbContextFactory<DefaultContext> contextFactory, ILogger logger)
        : base(contextFactory, logger)
    {
    }

    public virtual Task<PaginatedResult<T>> SearchAsync(string query, int pageNumber = 1, int pageSize = 50)
        => SearchAsync(query, pageNumber, pageSize, sort: null, options: null);

    public virtual Task<PaginatedResult<T>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort)
        => SearchAsync(query, pageNumber, pageSize, sort, options: null);

    public virtual Task<PaginatedResult<T>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort, TOptions? options)
        => SearchFilteredAsync(query, pageNumber, pageSize, sort, q => ApplySearchFilters(q, query, options));

    /// <summary>Narrows <paramref name="queryable"/> to rows matching <paramref name="query"/> and
    /// <paramref name="options"/>; null options means what the repository documents for it.</summary>
    protected abstract IQueryable<T> ApplySearchFilters(IQueryable<T> queryable, string query, TOptions? options);
}
