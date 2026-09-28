using System.Linq.Expressions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KHost.DataAccess.Repositories;

internal class VisualisationPlaylistRepository : BaseRepository<VisualisationPlaylist>, IVisualisationPlaylistRepository
{
    private static readonly IReadOnlyDictionary<string, Expression<Func<VisualisationPlaylist, object>>> _sortColumns =
        new Dictionary<string, Expression<Func<VisualisationPlaylist, object>>>
        {
            ["name"] = p => p.Name.ToLower(),
        };

    public VisualisationPlaylistRepository(IDbContextFactory<DefaultContext> contextFactory, ILogger<BaseRepository<VisualisationPlaylist>> logger)
        : base(contextFactory, logger)
    {
    }

    public async Task<VisualisationPlaylist?> ReadWithEntriesAsync(Guid id)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        return await context.VisualisationPlaylists
            .Include(p => p.Entries.OrderBy(e => e.Position))
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<IReadOnlyList<VisualisationPlaylist>> ReadAllWithEntriesAsync()
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        return await context.VisualisationPlaylists
            .Include(p => p.Entries.OrderBy(e => e.Position))
            .OrderBy(p => p.NameFolded)
            .ToListAsync();
    }

    public async Task ReplaceEntriesAsync(Guid playlistId, IReadOnlyList<VisualisationEntry> entries)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        var existing = await context.VisualisationEntries
            .Where(e => e.VisualisationPlaylistId == playlistId)
            .ToListAsync();

        context.VisualisationEntries.RemoveRange(existing);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            // A new column needs adding here as well as to the model, and nothing complains if it
            // is missed: the entry is copied, not attached.
            context.VisualisationEntries.Add(new VisualisationEntry
            {
                Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id,
                VisualisationPlaylistId = playlistId,
                Position = i,
                PresetSource = entry.PresetSource,
                PresetName = entry.PresetName,
                Brightness = entry.Brightness,
                Saturation = entry.Saturation,
                Sensitivity = entry.Sensitivity,
                DarkenBehindWords = entry.DarkenBehindWords,
                BarCount = entry.BarCount,
                ColourScheme = entry.ColourScheme,
                Colour = entry.Colour,
            });
        }

        await context.SaveChangesAsync();
    }

    protected override IReadOnlyDictionary<string, Expression<Func<VisualisationPlaylist, object>>> SortColumns => _sortColumns;
    protected override Expression<Func<VisualisationPlaylist, object>> DefaultSortExpression => p => p.Name.ToLower();

    protected override IQueryable<VisualisationPlaylist> ApplySearchFilters<TOptions>(IQueryable<VisualisationPlaylist> queryable, string query, TOptions? options = null)
        where TOptions : class
    {
        if (string.IsNullOrWhiteSpace(query))
            return queryable;

        return queryable.Where(p => EF.Functions.Like(p.NameFolded, FoldedContainsPattern(query), "\\"));
    }
}
