using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.DataAccess.Contexts;
using KHost.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KHost.DataAccess.Repositories;

internal class MediaRepository : BaseRepository<Media>, IMediaRepository
{
    private static readonly char[] _ftsMetaChars = ['"', '*', ':', '^', '(', ')', '+', '-'];

    private static readonly IReadOnlyDictionary<string, Expression<Func<Media, object>>> _sortColumns =
        new Dictionary<string, Expression<Func<Media, object>>>
        {
            ["title"] = m => m.Title,
            ["artist"] = m => m.Artist,
            ["format"] = m => m.Format,
            ["dateAdded"] = m => m.DateAdded,
            ["status"] = m => m.Status,
            ["duration"] = m => (object)(m.Duration ?? TimeSpan.Zero),
        };

    public MediaRepository(IDbContextFactory<DefaultContext> contextFactory, ILogger<BaseRepository<Media>> logger)
        : base(contextFactory, logger)
    {

    }

    public async Task<HashSet<string>> GetExistingFilePathsAsync(IEnumerable<string> filePaths)
    {
        var paths = filePaths.Select(p => p.ToLowerInvariant()).ToList();
        if (paths.Count == 0)
            return [];

        using var context = await ContextFactory.CreateDbContextAsync();
        var existing = await context.Media
            .Where(m => paths.Contains(m.FilePath.ToLower()))
            .Select(m => m.FilePath)
            .ToListAsync();

        return new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
    }

    protected override IReadOnlyDictionary<string, Expression<Func<Media, object>>> SortColumns => _sortColumns;
    protected override Expression<Func<Media, object>> DefaultSortExpression => m => m.Title;

    protected override IQueryable<Media> ApplySearchFilters<TOptions>(IQueryable<Media> queryable, string query, TOptions? options = null)
        where TOptions : class
    {
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

        var sw = Stopwatch.StartNew();
        try
        {
            var sql = $$"""
                SELECT m.*
                FROM "Media" AS m
                INNER JOIN "media_fts" AS f ON f."media_id" = m."Id"
                WHERE "media_fts" MATCH {0}
                ORDER BY bm25("media_fts")
                """;

            using var context = await ContextFactory.CreateDbContextAsync();

            var queryable = context.Media
                .FromSqlRaw(sql, match)
                .AsNoTracking();

            // The raw FTS query bypasses the base SearchAsync pipeline, so apply
            // the same option-based filters (e.g. status) here.
            queryable = ApplySearchFilters(queryable, query, options);

            var totalCount = await queryable.CountAsync();

            var items = await PaginationComponent
                .Paginate(queryable, pageNumber, pageSize)
                .ToListAsync();

            Logger.LogDebug("MediaRepository.SearchAsync q={Query} match={Match} elapsed={ElapsedMs}ms results={ResultCount} usedFts=true",
                query, match, sw.ElapsedMilliseconds, totalCount);

            return new PaginatedResult<Media>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }
        finally
        {
            KHostMetrics.MediaSearchDuration.Record(sw.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("used_fts", true));
        }
    }

    public override async Task<PaginatedResult<Media>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort)
    {
        var match = BuildFtsMatchExpression(query);

        if (match is null)
            return await base.SearchAsync(query, pageNumber, pageSize, sort);

        var sw = Stopwatch.StartNew();
        try
        {
            // FTS path: when sort is provided, override bm25 ordering
            var includeBm25InSql = sort is null;
            var sql = includeBm25InSql
                ? $$"""
                    SELECT m.*
                    FROM "Media" AS m
                    INNER JOIN "media_fts" AS f ON f."media_id" = m."Id"
                    WHERE "media_fts" MATCH {0}
                    ORDER BY bm25("media_fts")
                    """
                : $$"""
                    SELECT m.*
                    FROM "Media" AS m
                    INNER JOIN "media_fts" AS f ON f."media_id" = m."Id"
                    WHERE "media_fts" MATCH {0}
                    """;

            using var context = await ContextFactory.CreateDbContextAsync();

            IQueryable<Media> queryable = context.Media
                .FromSqlRaw(sql, match)
                .AsNoTracking();

            if (sort is not null)
                queryable = ApplySort(queryable, sort);

            var totalCount = await queryable.CountAsync();

            var items = await PaginationComponent
                .Paginate(queryable, pageNumber, pageSize)
                .ToListAsync();

            Logger.LogDebug("MediaRepository.SearchAsync q={Query} match={Match} sort={Sort} elapsed={ElapsedMs}ms results={ResultCount} usedFts=true",
                query, match, sort?.Column, sw.ElapsedMilliseconds, totalCount);

            return new PaginatedResult<Media>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }
        finally
        {
            KHostMetrics.MediaSearchDuration.Record(sw.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("used_fts", true));
        }
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
