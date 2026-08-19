using System.Linq.Expressions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KHost.DataAccess.Repositories;

internal class VenuesRepository : BaseRepository<Venue>, IVenuesRepository
{
    private static readonly IReadOnlyDictionary<string, Expression<Func<Venue, object>>> _sortColumns =
        new Dictionary<string, Expression<Func<Venue, object>>>
        {
            ["name"] = v => v.Name.ToLower(),
            ["enabled"] = v => v.Enabled,
        };

    public VenuesRepository(IDbContextFactory<DefaultContext> contextFactory, ILogger<BaseRepository<Venue>> logger)
        : base(contextFactory, logger)
    {
    }

    protected override IReadOnlyDictionary<string, Expression<Func<Venue, object>>> SortColumns => _sortColumns;
    protected override Expression<Func<Venue, object>> DefaultSortExpression => v => v.Name.ToLower();

    protected override IQueryable<Venue> ApplySearchFilters<TOptions>(IQueryable<Venue> queryable, string query, TOptions? options = null)
        where TOptions : class
    {
        if (string.IsNullOrWhiteSpace(query))
            return queryable;

        return queryable.Where(v => v.Name.ToLower().Contains(query.ToLower()));
    }
}
