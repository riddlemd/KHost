namespace KHost.DataAccess.Repositories;

using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;

internal class TipsRepository : BaseRepository<Tip>, ITipsRepository
{
    public TipsRepository(IDbContextFactory<DefaultContext> contextFactory)
        : base(contextFactory)
    {
    }

    public async Task<IReadOnlyList<Tip>> GetByUserIdAsync(Guid userId)
    {
        using var context = await ContextFactory.CreateDbContextAsync();
        return await context.Tips
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedDate)
            .ToListAsync();
    }

    public async Task<decimal> GetTotalByUserIdAsync(Guid userId, DateTime? from = null, DateTime? to = null)
    {
        using var context = await ContextFactory.CreateDbContextAsync();
        var query = context.Tips.Where(t => t.UserId == userId);
        if (from.HasValue) query = query.Where(t => t.CreatedDate >= from.Value);
        if (to.HasValue)   query = query.Where(t => t.CreatedDate <= to.Value);
        return await query.SumAsync(t => t.Amount);
    }

    protected override IQueryable<Tip> ApplySearchFilters<TOptions>(
        IQueryable<Tip> queryable, string query, TOptions? options = null)
        where TOptions : class
    {
        if (string.IsNullOrWhiteSpace(query))
            return queryable;

        queryable = queryable.Where(t => t.Notes.ToLower().Contains(query.ToLower()));
        return queryable.OrderByDescending(t => t.CreatedDate);
    }
}
