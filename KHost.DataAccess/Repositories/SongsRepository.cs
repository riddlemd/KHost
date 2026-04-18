using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.DataAccess.Contexts;
using KHost.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace KHost.DataAccess.Repositories;

public class SongsRepository : BaseRepository<ISong>
{
    private readonly IMediaFileParsingService _mediaFileParsingService;

    public SongsRepository(IDbContextFactory<SongLibraryContext> contextFactory, IMediaFileParsingService mediaFileParsingService)
        : base(contextFactory)
    {
        _mediaFileParsingService = mediaFileParsingService;
    }

    public async Task<ISong> CreateAsync(string filePath)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        var existing = await context.Songs.FirstOrDefaultAsync(s => s.FilePath == filePath);

        if (existing != null)
            return existing;

        var song = _mediaFileParsingService.LoadAndParse(filePath);
        context.Songs.Add(song);
        await context.SaveChangesAsync();

        return song;
    }

    public override async Task UpdateAsync(ISong song)
    {
        song.DateModified = DateTime.UtcNow;
        await base.UpdateAsync(song);
    }

    protected override IQueryable<ISong> ApplySearchFilters<TOptions>(IQueryable<ISong> queryable, string query, TOptions? options = null)
        where TOptions : class
    {
        if (string.IsNullOrWhiteSpace(query))
            return queryable;

        var statusesToReturn = options as HashSet<SongStatus>;

        queryable = queryable.Where(s => s.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                                         s.Artist.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                                         s.Album.Contains(query, StringComparison.CurrentCultureIgnoreCase));

        if (statusesToReturn?.Count > 0)
        {
            queryable = queryable.Where(s => statusesToReturn.Contains(s.Status));
        }

        return queryable.OrderBy(s => s.Title).ThenBy(s => s.Artist);
    }
}
