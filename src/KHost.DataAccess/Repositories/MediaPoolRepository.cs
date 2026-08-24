using System.Linq.Expressions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KHost.DataAccess.Repositories;

internal class MediaPoolRepository : BaseRepository<MediaPool>, IMediaPoolRepository
{
    private static readonly IReadOnlyDictionary<string, Expression<Func<MediaPool, object>>> _sortColumns =
        new Dictionary<string, Expression<Func<MediaPool, object>>>
        {
            ["name"] = p => p.Name.ToLower(),
            ["kind"] = p => p.Kind,
        };

    public MediaPoolRepository(IDbContextFactory<DefaultContext> contextFactory, ILogger<BaseRepository<MediaPool>> logger)
        : base(contextFactory, logger)
    {
    }

    public async Task<MediaPool?> ReadWithEntriesAsync(Guid id)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        return await context.MediaPools
            .Include(p => p.Entries.OrderBy(e => e.Position))
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<IReadOnlyList<MediaPool>> ReadAllWithEntriesAsync(MediaKind kind, Guid? venueId)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        // A pool with no venue belongs to every venue, so it is always in scope alongside the
        // ones this venue owns.
        return await context.MediaPools
            .Where(p => p.Kind == kind && (p.VenueId == null || p.VenueId == venueId))
            .Include(p => p.Entries.OrderBy(e => e.Position))
            .ToListAsync();
    }

    public async Task ReplaceEntriesAsync(Guid poolId, IReadOnlyList<MediaPoolEntry> entries)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        var existing = await context.MediaPoolEntries
            .Where(e => e.MediaPoolId == poolId)
            .ToListAsync();

        context.MediaPoolEntries.RemoveRange(existing);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            // Position is assigned from the order handed in rather than trusted off the model:
            // the editor reorders by moving rows, and only this list knows the result.
            context.MediaPoolEntries.Add(new MediaPoolEntry
            {
                Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id,
                MediaPoolId = poolId,
                Position = i,
                Weight = entry.Weight,
                MediaId = entry.MediaId,
                ChildPoolId = entry.ChildPoolId,
            });
        }

        await context.SaveChangesAsync();
    }

    protected override IReadOnlyDictionary<string, Expression<Func<MediaPool, object>>> SortColumns => _sortColumns;
    protected override Expression<Func<MediaPool, object>> DefaultSortExpression => p => p.Name.ToLower();

    protected override IQueryable<MediaPool> ApplySearchFilters<TOptions>(IQueryable<MediaPool> queryable, string query, TOptions? options = null)
        where TOptions : class
    {
        if (options is MediaPoolSearchOptions poolOptions)
        {
            if (poolOptions.Kind is { } kind)
                queryable = queryable.Where(p => p.Kind == kind);

            if (poolOptions.VenueId is { } venueId)
                queryable = queryable.Where(p => p.VenueId == null || p.VenueId == venueId);
        }

        if (string.IsNullOrWhiteSpace(query))
            return queryable;

        return queryable.Where(p => EF.Functions.Like(p.NameFolded, FoldedContainsPattern(query), "\\"));
    }
}
