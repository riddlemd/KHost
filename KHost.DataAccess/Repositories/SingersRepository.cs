using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;

namespace KHost.DataAccess.Repositories;

internal class SingersRepository : BaseRepository<Singer>, ISingersRepository
{
    public SingersRepository(IDbContextFactory<DefaultContext> contextFactory)
        : base(contextFactory)
    {
    }

    protected override IQueryable<Singer> ApplySearchFilters<TOptions>(IQueryable<Singer> queryable, string query, TOptions? options = null)
        where TOptions : class
    {
        if (string.IsNullOrWhiteSpace(query))
            return queryable;

        queryable = queryable.Where(s => s.Name.ToLower().Contains(query.ToLower()));

        return queryable.OrderBy(s => s.Name);
    }
}
