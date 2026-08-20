using System.Diagnostics;
using System.Linq.Expressions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories.Components;
using KHost.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KHost.DataAccess.Repositories;

internal abstract class BaseRepository<T> : IRepository<T> where T : RepositoryModel
{
    private static readonly string _entityTypeName = typeof(T).Name;

    protected readonly IDbContextFactory<DefaultContext> ContextFactory;
    protected readonly ILogger Logger;
    protected readonly SearchableDbSetComponent<T, DefaultContext> SearchableComponent;
    protected readonly PaginationComponent<T> PaginationComponent;

    protected BaseRepository(IDbContextFactory<DefaultContext> contextFactory, ILogger logger, int defaultPageSize = 50, int maxPageSize = 1000)
    {
        ContextFactory = contextFactory;
        Logger = logger;
        PaginationComponent = new(maxPageSize, defaultPageSize);
        SearchableComponent = new(ContextFactory, PaginationComponent);
    }

    protected async Task<DefaultContext> GetContextAsync()
        => await ContextFactory.CreateDbContextAsync();

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

        return PaginationComponent.BuildResult(items, totalCount, pageNumber, pageSize);
    }

    /// <summary>
    /// The text a free-text query is matched against. A repository that returns fields here gets
    /// its matching done in .NET instead of SQL, because the bundled SQLite folds ASCII only: a
    /// stored "ZOË" never matches a typed "zoë" through lower() or NOCASE, and the miss is silent.
    /// Null means the repository has no free-text search.
    /// </summary>
    protected virtual Func<T, IEnumerable<string?>>? SearchableTextFields => null;

    /// <summary>
    /// Streams the shaped query and keeps only the rows that match, so peak memory is the size of
    /// the result set rather than the table.
    /// </summary>
    private async Task<PaginatedResult<T>> SearchTextInMemoryAsync(
        string query, int pageNumber, int pageSize, Func<IQueryable<T>, IQueryable<T>> shape)
    {
        var fields = SearchableTextFields!;

        using var context = await ContextFactory.CreateDbContextAsync();

        var matches = new List<T>();
        await foreach (var entity in shape(context.Set<T>()).AsAsyncEnumerable())
        {
            if (fields(entity).Any(field => field is not null
                    && field.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                matches.Add(entity);
            }
        }

        var (page, size) = PaginationComponent.Normalize(pageNumber, pageSize);
        var items = matches.Skip((page - 1) * size).Take(size).ToList();

        return PaginationComponent.BuildResult(items, matches.Count, pageNumber, pageSize);
    }

    private Task<PaginatedResult<T>> RunSearchAsync(
        string query, int pageNumber, int pageSize, Func<IQueryable<T>, IQueryable<T>> shape)
        => string.IsNullOrWhiteSpace(query) || SearchableTextFields is null
            ? SearchableComponent.SearchAsync(query, pageNumber, pageSize, shape)
            : SearchTextInMemoryAsync(query, pageNumber, pageSize, shape);

    public virtual async Task<PaginatedResult<T>> SearchAsync<TOptions>(string query, int pageNumber = 0, int pageSize = 0, TOptions? options = null)
        where TOptions : class
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await RunSearchAsync(query, pageNumber, pageSize,
                q => ApplySort(ApplySearchFilters(q, query, options), sort: null));
            Logger.LogDebug("SearchAsync<{EntityType}> q={Query} elapsed={ElapsedMs}ms results={ResultCount}",
                _entityTypeName, query, sw.ElapsedMilliseconds, result.TotalCount);
            return result;
        }
        finally
        {
            KHostMetrics.SearchDuration.Record(sw.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("entity", _entityTypeName),
                new KeyValuePair<string, object?>("used_fts", false));
        }
    }

    public virtual Task<PaginatedResult<T>> SearchAsync(string query, int pageNumber = 0, int pageSize = 0)
        => SearchAsync(query, pageNumber, pageSize, sort: null);

    public virtual async Task<PaginatedResult<T>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await RunSearchAsync(query, pageNumber, pageSize,
                q => ApplySort(ApplySearchFilters<object>(q, query, null), sort));
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
