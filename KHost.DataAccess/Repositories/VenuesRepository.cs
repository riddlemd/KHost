using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;

namespace KHost.DataAccess.Repositories;

internal class VenuesRepository : BaseRepository<Venue>, IVenuesRepository
{
    public VenuesRepository(IDbContextFactory<DefaultContext> contextFactory)
        : base(contextFactory)
    {
    }

    protected override IQueryable<Venue> ApplySearchFilters<TOptions>(IQueryable<Venue> queryable, string query, TOptions? options = null)
        where TOptions : class
    {
        if (string.IsNullOrWhiteSpace(query))
            return queryable;

        queryable = queryable.Where(v => v.Name.ToLower().Contains(query.ToLower()));

        return queryable.OrderBy(v => v.Name);
    }
}
