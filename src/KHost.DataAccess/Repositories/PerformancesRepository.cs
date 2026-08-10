using System.Linq.Expressions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KHost.DataAccess.Repositories;

internal class PerformancesRepository : BaseRepository<Performance>, IPerformancesRepository
{
    private static readonly IReadOnlyDictionary<string, Expression<Func<Performance, object>>> _sortColumns =
        new Dictionary<string, Expression<Func<Performance, object>>>
        {
            ["createdDate"] = p => p.CreatedDate,
            ["queuePosition"] = p => p.QueuePosition ?? 0,
        };

    public PerformancesRepository(IDbContextFactory<DefaultContext> contextFactory, ILogger<BaseRepository<Performance>> logger)
        : base(contextFactory, logger)
    {
    }

    public async Task<int> ReadNextQueuePositionForSingerAsync(Guid singerId)
    {
        var context = await GetContextAsync();
        var positions = await context.Set<Performance>()
            .Where(p => p.SingerId == singerId && p.QueuePosition.HasValue)
            .Select(p => p.QueuePosition!.Value)
            .ToListAsync();
        return (positions.DefaultIfEmpty(0).Max()) + 1;
    }

    public async Task<Performance?> ReadSingersNextPerformanceAsync(Guid singerId)
        => (await GetContextAsync()).Set<Performance>()
            .Where(p => p.SingerId == singerId && p.QueuePosition.HasValue)
            .OrderBy(p => p.QueuePosition)
            .FirstOrDefault();

    public async Task<List<Performance>> ReadQueuedAsync()
    {
        using var context = await ContextFactory.CreateDbContextAsync();
        return await context.Set<Performance>()
            .Where(p => p.QueuePosition != null)
            .OrderBy(p => p.QueuePosition)
            .ToListAsync();
    }

    public override Task<PaginatedResult<Performance>> ReadAllAsync(int pageNumber = 0, int pageSize = 0)
        => ReadAllAsync(pageNumber, pageSize, PerformanceFilter.All);

    public async Task<PaginatedResult<Performance>> ReadAllAsync(int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.All)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        var query = ApplyFilter(context.Set<Performance>(), filter)
            .OrderBy(p => p.QueuePosition);

        var totalCount = await query.CountAsync();

        var items = await PaginationComponent
            .Paginate(query, pageNumber, pageSize)
            .ToListAsync();

        return PaginationComponent.BuildResult(items, totalCount, pageNumber, pageSize);
    }

    public async Task<PaginatedResult<Performance>> ReadBySingerIdAsync(Guid singerId, int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.All)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        var query = context.Set<Performance>()
            .Where(p => p.SingerId == singerId);

        query = ApplyFilter(query, filter);

        query = query.OrderByDescending(p => p.CreatedDate);

        var totalCount = await query.CountAsync();

        var items = await PaginationComponent
            .Paginate(query, pageNumber, pageSize)
            .ToListAsync();

        return PaginationComponent.BuildResult(items, totalCount, pageNumber, pageSize);
    }

    public async Task<PaginatedResult<Performance>> ReadByMediaIdAsync(Guid mediaId, int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.All)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        var query = context.Set<Performance>()
            .Where(p => p.MediaId == mediaId);

        query = ApplyFilter(query, filter);

        query = query.OrderByDescending(p => p.CreatedDate);

        var totalCount = await query.CountAsync();

        var items = await PaginationComponent
            .Paginate(query, pageNumber, pageSize)
            .ToListAsync();

        return PaginationComponent.BuildResult(items, totalCount, pageNumber, pageSize);
    }

    public async Task DeleteAllQueuedAsync()
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        await context.Set<Performance>()
            .Where(p => p.QueuePosition.HasValue)
            .ExecuteDeleteAsync();
    }

    internal static IQueryable<Performance> ApplyFilter(IQueryable<Performance> query, PerformanceFilter filter)
    {
        bool wantQueued   = filter.HasFlag(PerformanceFilter.Queued);
        bool wantUnQueued = filter.HasFlag(PerformanceFilter.UnQueued);

        if (wantQueued && !wantUnQueued)
            return query.Where(p => p.QueuePosition != null);

        if (!wantQueued && wantUnQueued)
            return query.Where(p => p.QueuePosition == null);

        return query;
    }

    protected override IReadOnlyDictionary<string, Expression<Func<Performance, object>>> SortColumns => _sortColumns;
    protected override Expression<Func<Performance, object>> DefaultSortExpression => p => p.CreatedDate;
    protected override bool DefaultSortDescending => true;

    protected override IQueryable<Performance> ApplySearchFilters<TOptions>(IQueryable<Performance> queryable, string query, TOptions? options = null)
        where TOptions : class
    {
        return queryable;
    }
}
