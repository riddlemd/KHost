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

    // Linux filesystems are case-sensitive; Windows and default macOS volumes are not. Folding case
    // on Linux would treat Song.mp4 and song.mp4 as the same file and silently drop one on import.
    private static readonly bool _caseSensitivePaths = OperatingSystem.IsLinux();

    private static readonly IReadOnlyDictionary<string, Expression<Func<Media, object>>> _sortColumns =
        new Dictionary<string, Expression<Func<Media, object>>>
        {
            ["title"] = m => m.Title.ToLower(),
            ["artist"] = m => m.Artist.ToLower(),
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
        var comparer = _caseSensitivePaths ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var wanted = new HashSet<string>(filePaths, comparer);
        if (wanted.Count == 0)
            return [];

        using var context = await ContextFactory.CreateDbContextAsync();

        // Byte-exact is both what a case-sensitive filesystem means and what SQLite's default
        // BINARY collation does, so the unique index on FilePath serves this query directly.
        if (_caseSensitivePaths)
        {
            var lookup = wanted.ToList();
            var exact = await context.Media
                .Where(m => lookup.Contains(m.FilePath))
                .Select(m => m.FilePath)
                .ToListAsync();

            return new HashSet<string>(exact, comparer);
        }

        // Bundled SQLite (e_sqlite3, no ICU) folds ASCII only, so "SÖNG.mp4" would not match a
        // scanned "söng.mp4" and the re-import would trip the unique index. Let .NET compare.
        var found = new HashSet<string>(comparer);
        await foreach (var stored in context.Media.Select(m => m.FilePath).AsAsyncEnumerable())
        {
            if (wanted.Contains(stored))
                found.Add(stored);
        }

        return found;
    }

    public async Task<IReadOnlyList<Media>> GetByFileSizesAsync(IEnumerable<long> sizes)
    {
        var wanted = sizes.Distinct().ToList();
        if (wanted.Count == 0)
            return [];

        using var context = await ContextFactory.CreateDbContextAsync();

        return await context.Media
            .Where(m => m.FileSize != null && wanted.Contains(m.FileSize.Value))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Media>> GetWithoutFileSizeAsync()
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        return await context.Media
            .Where(m => m.FileSize == null)
            .ToListAsync();
    }

    public async Task UpdateFingerprintsAsync(IEnumerable<Media> media)
    {
        var rows = media.ToList();
        if (rows.Count == 0)
            return;

        using var context = await ContextFactory.CreateDbContextAsync();

        // Marking single properties rather than calling Update: the context is NoTracking, so a
        // whole-entity update would write back stale copies of every other column.
        foreach (var row in rows)
        {
            var entry = context.Attach(row);
            entry.Property(m => m.FileSize).IsModified = true;
            entry.Property(m => m.SampledHash).IsModified = true;
            entry.Property(m => m.ContentHash).IsModified = true;
        }

        try
        {
            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "EF SaveChanges failed updating fingerprints for {Count} media rows", rows.Count);
            throw;
        }
    }

    protected override IReadOnlyDictionary<string, Expression<Func<Media, object>>> SortColumns => _sortColumns;
    protected override Expression<Func<Media, object>> DefaultSortExpression => m => m.Title.ToLower();

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

            return PaginationComponent.BuildResult(items, totalCount, pageNumber, pageSize);
        }
        finally
        {
            KHostMetrics.SearchDuration.Record(sw.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("entity", nameof(Media)),
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
            // An explicit sort supersedes relevance rather than combining with it: the caller asked
            // for that column's order, and bm25 could only break ties within it.
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

            return PaginationComponent.BuildResult(items, totalCount, pageNumber, pageSize);
        }
        finally
        {
            KHostMetrics.SearchDuration.Record(sw.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("entity", nameof(Media)),
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
