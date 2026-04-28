using System.Linq.Expressions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories.Components;
using Microsoft.EntityFrameworkCore;

namespace KHost.DataAccess.Repositories;

internal abstract class BaseRepository<T> : IRepository<T> where T : class
{
    protected readonly IDbContextFactory<DefaultContext> ContextFactory;
    protected readonly SearchableDbSetComponent<T, DefaultContext> SearchableComponent;
    protected readonly PaginationComponent<T> PaginationComponent;

    protected BaseRepository(IDbContextFactory<DefaultContext> contextFactory, int defaultPageSize = 50, int maxPageSize = 1000)
    {
        ContextFactory = contextFactory;
        PaginationComponent = new(maxPageSize, defaultPageSize);
        SearchableComponent = new(ContextFactory, PaginationComponent);
    }

    protected async Task<DefaultContext> GetContextAsync()
        => await ContextFactory.CreateDbContextAsync();

    public virtual async Task<T> CreateAsync(T entity)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        context.Set<T>().Add(entity);

        await context.SaveChangesAsync();

        return entity;
    }

    public virtual async Task<T?> ReadAsync(Guid id)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        return await context.Set<T>().FirstOrDefaultAsync(e => EF.Property<Guid>(e, "Id") == id);
    }

    public virtual async Task UpdateAsync(T entity)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        context.Set<T>().Update(entity);

        await context.SaveChangesAsync();
    }

    public virtual async Task<bool> DeleteAsync(Guid id)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        var entity = await context.Set<T>().FirstOrDefaultAsync(e => EF.Property<Guid>(e, "Id") == id);

        if (entity is null)
            return false;

        context.Set<T>().Remove(entity);

        await context.SaveChangesAsync();

        return true;
    }

    public virtual async Task<PaginatedResult<T>> ReadAllAsync(int pageNumber = 0, int pageSize = 0)
    {
        return await ReadAllAsync(pageNumber, pageSize, sort: null);
    }

    public virtual async Task<PaginatedResult<T>> ReadAllAsync(int pageNumber, int pageSize, SortDescriptor? sort)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        var totalCount = await context.Set<T>().CountAsync();

        var items = await PaginationComponent
            .Paginate(ApplySort(context.Set<T>(), sort), pageNumber, pageSize)
            .ToListAsync();

        return new PaginatedResult<T>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public virtual async Task<PaginatedResult<T>> SearchAsync<TOptions>(string query, int pageNumber = 0, int pageSize = 0, TOptions? options = null)
        where TOptions : class
        => await SearchableComponent.SearchAsync(query, pageNumber, pageSize,
            q => ApplySort(ApplySearchFilters(q, query, options), sort: null));

    public virtual Task<PaginatedResult<T>> SearchAsync(string query, int pageNumber = 0, int pageSize = 0)
        => SearchAsync(query, pageNumber, pageSize, sort: null);

    public virtual Task<PaginatedResult<T>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort)
        => SearchableComponent.SearchAsync(query, pageNumber, pageSize,
            q => ApplySort(ApplySearchFilters<object>(q, query, null), sort));

    public virtual async Task<bool> HasAnyAsync()
    {
        using var context = await ContextFactory.CreateDbContextAsync();
        return await context.Set<T>().AnyAsync();
    }

    protected abstract IQueryable<T> ApplySearchFilters<TOptions>(IQueryable<T> queryable, string query, TOptions? options = null)
        where TOptions : class;

    protected abstract IReadOnlyDictionary<string, Expression<Func<T, object>>> SortColumns { get; }
    protected abstract Expression<Func<T, object>> DefaultSortExpression { get; }
    protected virtual bool DefaultSortDescending => false;

    protected IQueryable<T> ApplySort(IQueryable<T> queryable, SortDescriptor? sort)
    {
        Expression<Func<T, object>> expr;
        bool descending;

        if (sort is not null && SortColumns.TryGetValue(sort.Column, out var colExpr))
        {
            expr = colExpr;
            descending = sort.Descending;
        }
        else
        {
            expr = DefaultSortExpression;
            descending = DefaultSortDescending;
        }

        return descending ? queryable.OrderByDescending(expr) : queryable.OrderBy(expr);
    }
}
