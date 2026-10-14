using System.Diagnostics;
using System.Linq.Expressions;
using KHost.Abstractions.Models;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories.Components;
using KHost.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KHost.DataAccess.Repositories;

internal abstract class BaseRepository<T> : IRepository<T> where T : RepositoryModel
{
    private static readonly string _entityTypeName = typeof(T).Name;

    private readonly SearchableDbSetComponent<T, DefaultContext> _searchableComponent;

    protected readonly IDbContextFactory<DefaultContext> ContextFactory;
    protected readonly ILogger Logger;
    protected readonly PaginationComponent<T> PaginationComponent;

    protected BaseRepository(IDbContextFactory<DefaultContext> contextFactory, ILogger logger, int defaultPageSize = 50, int maxPageSize = 1000)
    {
        ContextFactory = contextFactory;
        Logger = logger;
        PaginationComponent = new(maxPageSize, defaultPageSize);
        _searchableComponent = new(ContextFactory, PaginationComponent);
    }

    public virtual async Task<T> CreateAsync(T entity)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        context.Set<T>().Add(entity);

        try
        {
            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "EF SaveChanges failed creating {EntityType}", _entityTypeName);
            throw;
        }

        Logger.LogDebug("Created {EntityType} {EntityId}", _entityTypeName, entity.Id);
        return entity;
    }

    public virtual async Task<T?> ReadAsync(Guid id)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        return await context.Set<T>().FirstOrDefaultAsync(e => e.Id == id);
    }

    public virtual async Task UpdateAsync(T entity)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        context.Set<T>().Update(entity);

        try
        {
            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "EF SaveChanges failed updating {EntityType} {EntityId}", _entityTypeName, entity.Id);
            throw;
        }

        Logger.LogDebug("Updated {EntityType} {EntityId}", _entityTypeName, entity.Id);
    }

    public virtual async Task<bool> DeleteAsync(Guid id)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        var entity = await context.Set<T>().FirstOrDefaultAsync(e => e.Id == id);

        if (entity is null)
        {
            Logger.LogInformation("DeleteAsync<{EntityType}>({EntityId}) — entity not found", _entityTypeName, id);
            return false;
        }

        context.Set<T>().Remove(entity);

        try
        {
            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "EF SaveChanges failed deleting {EntityType} {EntityId}", _entityTypeName, id);
            throw;
        }

        Logger.LogDebug("Deleted {EntityType} {EntityId}", _entityTypeName, id);
        return true;
    }

    public virtual async Task<PaginatedResult<T>> ReadAllAsync(int pageNumber = 1, int pageSize = 0)
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

        return PaginationComponent.BuildResult(items, totalCount, pageNumber, pageSize);
    }

    /// <summary>How this repository folds text. Media adds stylisations; everything else does not.</summary>
    protected virtual Func<string?, string> Folder => EntityFolding.Fold;

    /// <summary>Builds a contains-pattern for a folded column, folding the query the same way.</summary>
    /// <remarks>Escapes LIKE's own wildcards, so a singer called "50%" searches for itself.</remarks>
    protected string FoldedContainsPattern(string query)
    {
        var folded = Folder(query)
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");

        return $"%{folded}%";
    }

    public virtual async Task<bool> HasAnyAsync()
    {
        using var context = await ContextFactory.CreateDbContextAsync();
        return await context.Set<T>().AnyAsync();
    }

    /// <summary>Runs one text search through <paramref name="filter"/>, timed and recorded.</summary>
    /// <remarks>For a subclass that implements <see cref="ISearchable{T}"/>; this class offers none.</remarks>
    protected async Task<PaginatedResult<T>> SearchFilteredAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort,
        Func<IQueryable<T>, IQueryable<T>> filter)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _searchableComponent.SearchAsync(query, pageNumber, pageSize, q => ApplySort(filter(q), sort));
            Logger.LogDebug("SearchAsync<{EntityType}> q={Query} sort={Sort} elapsed={ElapsedMs}ms results={ResultCount}",
                _entityTypeName, query, sort?.Column, sw.ElapsedMilliseconds, result.TotalCount);
            return result;
        }
        finally
        {
            KHostMetrics.SearchDuration.Record(sw.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("entity", _entityTypeName),
                new KeyValuePair<string, object?>("used_fts", false));
        }
    }

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
