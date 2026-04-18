using KHost.DataAccess.Contexts;
using KHost.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace KHost.DataAccess.Repositories;

public abstract class BaseRepository<T> where T : class
{
    protected readonly IDbContextFactory<SongLibraryContext> ContextFactory;

    protected int DefaultPageSize { get; set; } = 50;
    protected int MaxPageSize { get; set; } = 1000;

    protected BaseRepository(IDbContextFactory<SongLibraryContext> contextFactory)
    {
        ContextFactory = contextFactory;
    }

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
        return await context.Set<T>().FindAsync(id);
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
        var entity = await context.Set<T>().FindAsync(id);
        if (entity is null)
            return false;

        context.Set<T>().Remove(entity);
        await context.SaveChangesAsync();
        return true;
    }

    public virtual async Task<PaginatedResult<T>> ReadAllAsync(int pageNumber = 1, int pageSize = 0)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        using var context = await ContextFactory.CreateDbContextAsync();

        var totalCount = await context.Set<T>().CountAsync();

        var items = await context.Set<T>()
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PaginatedResult<T>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public virtual async Task<PaginatedResult<T>> SearchAsync<TOptions>(string query, int pageNumber = 1, int pageSize = 50, TOptions? options = null)
        where TOptions : class
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        using var context = await ContextFactory.CreateDbContextAsync();

        IQueryable<T> queryable = context.Set<T>();
        queryable = ApplySearchFilters(queryable, query, options);

        var totalCount = await queryable.CountAsync();

        var items = await queryable
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PaginatedResult<T>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    protected abstract IQueryable<T> ApplySearchFilters<TOptions>(IQueryable<T> queryable, string query, TOptions? options = null)
        where TOptions : class;
}
