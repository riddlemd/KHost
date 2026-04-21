using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;

namespace KHost.DataAccess.Repositories;

internal class MediaRepository : BaseRepository<Media>, IMediaRepository
{
    public MediaRepository(IDbContextFactory<DefaultContext> contextFactory)
        : base(contextFactory)
    {

    }

    protected override IQueryable<Media> ApplySearchFilters<TOptions>(IQueryable<Media> queryable, string query, TOptions? options = null)
        where TOptions : class
    {
        queryable = queryable
            .OrderBy(s => s.Title)
            .ThenBy(s => s.Artist);

        if (string.IsNullOrWhiteSpace(query))
            return queryable;

        queryable = queryable.Where(s => s.Title.ToLower().Contains(query.ToLower()) || s.Artist.ToLower().Contains(query.ToLower()));

        var statusesToReturn = options as HashSet<MediaStatus>;
        if (statusesToReturn?.Count > 0)
            queryable = queryable.Where(s => statusesToReturn.Contains(s.Status));

        return queryable;
    }
}
