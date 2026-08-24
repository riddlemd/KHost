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
    private const int TrigramLength = 3;

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

    public async Task<Media?> FindByFilePathAsync(string filePath)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        if (_caseSensitivePaths)
            return await context.Media.FirstOrDefaultAsync(m => m.FilePath == filePath);

        // Same reasoning as GetExistingFilePathsAsync: bundled SQLite folds ASCII only, so this
        // has to compare in .NET rather than push a lower() down into the query.
        await foreach (var row in context.Media.AsAsyncEnumerable())
        {
            if (string.Equals(row.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                return row;
        }

        return null;
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

    private static IQueryable<Media> ApplyKindAndStatus(IQueryable<Media> queryable, MediaSearchOptions? options)
    {
        options ??= MediaSearchOptions.Default;

        if (options.Kind is { } kind)
            queryable = queryable.Where(m => m.Kind == kind);

        var statuses = options.Statuses;
        if (statuses?.Count > 0)
            queryable = queryable.Where(m => statuses.Contains(m.Status));

        return queryable;
    }

    public override Task<PaginatedResult<Media>> ReadAllAsync(int pageNumber, int pageSize, SortDescriptor? sort)
        => ReadAllAsync(pageNumber, pageSize, sort, options: null);

    public async Task<PaginatedResult<Media>> ReadAllAsync(int pageNumber, int pageSize, SortDescriptor? sort, MediaSearchOptions? options)
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        // Filtered before the count as well as the page: a total that counts ads would page the
        // library past its own last row.
        var queryable = ApplyKindAndStatus(context.Media, options);

        var totalCount = await queryable.CountAsync();

        var items = await PaginationComponent
            .Paginate(ApplySort(queryable, sort), pageNumber, pageSize)
            .ToListAsync();

        return PaginationComponent.BuildResult(items, totalCount, pageNumber, pageSize);
    }

    public override async Task<bool> HasAnyAsync()
    {
        using var context = await ContextFactory.CreateDbContextAsync();

        // A library holding nothing but ads is still a host with no songs to sing, which is what
        // the setup wizard is asking about.
        return await context.Media.AnyAsync(m => m.Kind == MediaKind.Karaoke);
    }

    // Media queries fold exactly the way media rows were folded on the way in.
    protected override Func<string?, string> Folder => EntityFolding.FoldMedia;

    protected override IReadOnlyDictionary<string, Expression<Func<Media, object>>> SortColumns => _sortColumns;
    protected override Expression<Func<Media, object>> DefaultSortExpression => m => m.Title.ToLower();

    protected override IQueryable<Media> ApplySearchFilters<TOptions>(IQueryable<Media> queryable, string query, TOptions? options = null)
        where TOptions : class
    {
        // Anything that is not MediaSearchOptions — including the null the sort-only overloads
        // pass — lands on the default, which is karaoke. Forgetting to pass options narrows the
        // result rather than widening it.
        queryable = ApplyKindAndStatus(queryable, options as MediaSearchOptions);

        // Only reached when the query cannot go to FTS - a term too short for the trigram index,
        // or one left empty once the metacharacters were stripped.
        if (string.IsNullOrWhiteSpace(query))
            return queryable;

        return queryable.Where(m => EF.Functions.Like(m.SearchFolded, FoldedContainsPattern(query), "\\"));
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

    public override Task<PaginatedResult<Media>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort)
        => SearchAsync(query, pageNumber, pageSize, sort, options: null);

    public async Task<PaginatedResult<Media>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort, MediaSearchOptions? options)
    {
        var match = BuildFtsMatchExpression(query);

        // Neither base overload carries a sort and options at once, so the fallback is built here
        // rather than delegated — handing options to base.SearchAsync picks the generic overload
        // and silently drops the sort.
        if (match is null)
        {
            return await SearchableComponent.SearchAsync(query, pageNumber, pageSize,
                q => ApplySort(ApplySearchFilters(q, query, options ?? MediaSearchOptions.Default), sort));
        }

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

            // The FTS join matches on text alone and knows nothing of kind, so the filter is
            // composed onto the raw query the same way the other paths apply it.
            queryable = ApplyKindAndStatus(queryable, options);

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

    private string? BuildFtsMatchExpression(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        // Folded exactly the way the indexed text was: the index holds SearchFolded, so a query
        // matches by being reduced with the same rules, not by either side being clever.
        var folded = Folder(query);

        var sanitized = new StringBuilder(query.Length);

        foreach (var ch in folded)
        {
            if (Array.IndexOf(_ftsMetaChars, ch) < 0)
                sanitized.Append(ch);
        }

        var tokens = sanitized.ToString()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // The trigram index cannot represent a term shorter than three characters, so a query with
        // one would match nothing at all rather than too much. Hand those to the substring search.
        if (tokens.Length == 0 || Array.Exists(tokens, token => token.Length < TrigramLength))
            return null;

        return string.Join(' ', tokens.Select(t => $"\"{t}\""));
    }
}
