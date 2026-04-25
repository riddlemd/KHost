using System.Globalization;
using System.Text;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;

namespace KHost.DataAccess.Repositories;

internal class MediaRepository : BaseRepository<Media>, IMediaRepository
{
    private static readonly char[] _ftsMetaChars = ['"', '*', ':', '^', '(', ')', '+', '-'];

    public MediaRepository(IDbContextFactory<DefaultContext> contextFactory)
        : base(contextFactory)
    {

    }

    public async Task<HashSet<string>> GetExistingFilePathsAsync(IEnumerable<string> filePaths)
    {
        var paths = filePaths.ToList();
        if (paths.Count == 0)
            return [];

        using var context = await ContextFactory.CreateDbContextAsync();
        var existing = await context.Media
            .Where(m => paths.Contains(m.FilePath))
            .Select(m => m.FilePath)
            .ToListAsync();

        return [..existing];
    }

    protected override IQueryable<Media> ApplySearchFilters<TOptions>(IQueryable<Media> queryable, string query, TOptions? options = null)
        where TOptions : class
    {
        queryable = queryable
            .OrderBy(m => m.Title)
            .ThenBy(m => m.Artist);

        var statusesToReturn = options as HashSet<MediaStatus>;
        if (statusesToReturn?.Count > 0)
            queryable = queryable.Where(m => statusesToReturn.Contains(m.Status));

        return queryable;
    }

    public override async Task<PaginatedResult<Media>> SearchAsync<TOptions>(string query, int pageNumber = 0, int pageSize = 0, TOptions? options = null)
        where TOptions : class
    {
        var match = BuildFtsMatchExpression(query);

        if (match is null)
            return await base.SearchAsync(query, pageNumber, pageSize, options);

        var statusFilterSql = "";

        var sql = $$"""
            SELECT m.*
            FROM "Media" AS m
            INNER JOIN "media_fts" AS f ON f."media_id" = m."Id"
            WHERE "media_fts" MATCH {0}{{statusFilterSql}}
            ORDER BY bm25("media_fts")
            """;

        using var context = await ContextFactory.CreateDbContextAsync();

        var queryable = context.Media
            .FromSqlRaw(sql, match)
            .AsNoTracking();

        var totalCount = await queryable.CountAsync();

        var items = await PaginationComponent
            .Paginate(queryable, pageNumber, pageSize)
            .ToListAsync();

        return new PaginatedResult<Media>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    private static string? BuildFtsMatchExpression(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var sanitized = new StringBuilder(query.Length);

        foreach (var ch in query.ToLowerInvariant())
        {
            if (Array.IndexOf(_ftsMetaChars, ch) < 0)
                sanitized.Append(ch);
        }

        var tokens = sanitized.ToString()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
            return null;

        return string.Join(' ', tokens.Select(t => $"\"{t}\""));
    }
}
