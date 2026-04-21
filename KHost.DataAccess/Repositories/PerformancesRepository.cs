using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;

namespace KHost.DataAccess.Repositories;

internal class PerformancesRepository : BaseRepository<Performance>, IPerformancesRepository
{
    public PerformancesRepository(IDbContextFactory<DefaultContext> contextFactory)
        : base(contextFactory)
    {
    }

    public async Task<int> GetNextQueuePositionForSingerAsync(Guid singerId)
        => (await GetContextAsync()).Set<Performance>()
            .Where(p => p.SingerId == singerId && p.QueuePosition.HasValue)
            .Max(p => p.QueuePosition + 1) ?? 0;

    public async Task<Performance?> GetSingersNextPerformanceAsync(Guid singerId)
        => (await GetContextAsync()).Set<Performance>()
            .Where(p => p.SingerId == singerId && p.QueuePosition.HasValue)
            .OrderBy(p => p.QueuePosition)
            .FirstOrDefault();

    public async Task<PaginatedResult<Performance>> GetBySingerIdAsync(Guid singerId, int pageNumber = 1, int pageSize = 0, bool includeQueued = false)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        var query = context.Set<Performance>()
            .Where(p => p.SingerId == singerId);

        if (!includeQueued)
            query = query.Where(p => p.QueuePosition == null);

        query = query.OrderByDescending(p => p.CreatedDate);

        var totalCount = await query.CountAsync();

        var items = await PaginationComponent
            .Paginate(query, pageNumber, pageSize)
            .ToListAsync();

        return new PaginatedResult<Performance>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public async Task<PaginatedResult<Performance>> GetByMediaIdAsync(Guid mediaId, int pageNumber = 1, int pageSize = 0, bool includeQueued = false)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        var query = context.Set<Performance>()
            .Where(p => p.MediaId == mediaId);

        if (!includeQueued)
            query = query.Where(p => !p.QueuePosition.HasValue);

        query = query.OrderByDescending(p => p.CreatedDate);

        var totalCount = await query.CountAsync();

        var items = await PaginationComponent
            .Paginate(query, pageNumber, pageSize)
            .ToListAsync();

        return new PaginatedResult<Performance>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    protected override IQueryable<Performance> ApplySearchFilters<TOptions>(IQueryable<Performance> queryable, string query, TOptions? options = null)
        where TOptions : class
    {
        return queryable.OrderByDescending(p => p.CreatedDate);
    }
}
