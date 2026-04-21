using KHost.Abstractions.Models;
using Microsoft.EntityFrameworkCore;

namespace KHost.DataAccess.Repositories.Components;

internal class SearchableDbSetComponent<TClass, TContext>
    where TClass : class
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly PaginationComponent<TClass> _paginationComponent;

    public SearchableDbSetComponent(IDbContextFactory<TContext> contextFactory, int maxPageSize, int defaultPageSize)
    {
        _contextFactory = contextFactory;
        _paginationComponent = new(maxPageSize, defaultPageSize);
    }

    public SearchableDbSetComponent(IDbContextFactory<TContext> contextFactory, PaginationComponent<TClass> paginationComponent)
    {
        _contextFactory = contextFactory;
        _paginationComponent = paginationComponent;
    }

    public async Task<PaginatedResult<TClass>> SearchAsync(string query, int pageNumber = 1, int pageSize = 0, Func<IQueryable<TClass>, IQueryable<TClass>>? searchFilter = null)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        IQueryable<TClass> queryable = context.Set<TClass>();

        if (searchFilter is not null)
            queryable = searchFilter.Invoke(queryable) ?? queryable;

        var totalCount = await queryable.CountAsync();

        var items = await _paginationComponent
            .Paginate(queryable, pageNumber, pageSize)
            .ToListAsync();

        return new PaginatedResult<TClass>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }
}
