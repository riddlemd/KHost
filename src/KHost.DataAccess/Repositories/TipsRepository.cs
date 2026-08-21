namespace KHost.DataAccess.Repositories;

using System.Linq.Expressions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

internal class TipsRepository : BaseRepository<Tip>, ITipsRepository
{
    private static readonly IReadOnlyDictionary<string, Expression<Func<Tip, object>>> _sortColumns =
        new Dictionary<string, Expression<Func<Tip, object>>>
        {
            ["createdDate"] = t => t.CreatedDate,
            ["amount"] = t => t.AmountInCents,
            ["paymentMethod"] = t => t.PaymentMethod,
        };

    public TipsRepository(IDbContextFactory<DefaultContext> contextFactory, ILogger<BaseRepository<Tip>> logger)
        : base(contextFactory, logger)
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

    public async Task<int> GetTotalInCentsByUserIdAsync(Guid userId, DateTime? from = null, DateTime? to = null)
    {
        using var context = await ContextFactory.CreateDbContextAsync();
        var query = context.Tips.Where(t => t.UserId == userId);
        if (from.HasValue) query = query.Where(t => t.CreatedDate >= from.Value);
        if (to.HasValue)   query = query.Where(t => t.CreatedDate <= to.Value);
        return await query.SumAsync(t => t.AmountInCents);
    }

    protected override IReadOnlyDictionary<string, Expression<Func<Tip, object>>> SortColumns => _sortColumns;
    protected override Expression<Func<Tip, object>> DefaultSortExpression => t => t.CreatedDate;
    protected override bool DefaultSortDescending => true;

    protected override IQueryable<Tip> ApplySearchFilters<TOptions>(
        IQueryable<Tip> queryable, string query, TOptions? options = null)
        where TOptions : class
    {
        if (string.IsNullOrWhiteSpace(query))
            return queryable;

        return queryable.Where(t => EF.Functions.Like(t.NotesFolded, FoldedContainsPattern(query), "\\"));
    }
}
