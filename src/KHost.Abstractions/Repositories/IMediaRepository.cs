using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

/// <summary>The media library table: songs, break music, ad clips and stills alike.</summary>
/// <remarks>
/// <para>Host-implemented. A plugin reads and edits the library through
/// <see cref="Services.IMediaService"/>, which announces
/// <see cref="Messaging.Messages.MediaLibraryChanged"/> on every write and, on delete, first takes
/// the song out of every queue it waits in; deleting here leaves those queued turns pointing at
/// nothing. A plugin that downloads media uses <see cref="Services.IMediaAcquisitionService"/>,
/// which owns the import and status rules. The file-path and fingerprint members below serve the
/// host's own importer and have no service equivalent.</para>
/// <para>The plain <see cref="IRepository{T}"/> and <see cref="ISearchable{T}"/> overloads return
/// <see cref="MediaType.Karaoke"/> rows only; pass <see cref="MediaSearchOptions"/> to see other
/// types. <see cref="IRepository{T}.HasAnyAsync"/> likewise answers whether any karaoke row exists.</para>
/// <para>Search matches title and artist together, without regard to case or accents, and with
/// stylised spellings read as their plain form ("pink" finds "P!nk"). With no sort given, a query
/// whose every word has at least three characters comes back ranked by relevance; one with a
/// shorter word comes back in the default order. Sort keys: <c>title</c> (default), <c>artist</c>,
/// <c>format</c>, <c>dateAdded</c>, <c>status</c>, <c>duration</c>.</para>
/// </remarks>
public interface IMediaRepository : IRepository<Media>
{
    /// <summary>Listing and search that reach past karaoke; the plain overloads answer songs alone.</summary>
    /// <param name="pageNumber">1-based page to read; below 1 reads page 1.</param>
    /// <param name="pageSize">Rows per page; below 1 uses the default, and it is capped (see <see cref="ISearchable{T}"/>).</param>
    /// <param name="sort">A sort key listed above; null or unknown uses the default order.</param>
    /// <param name="options">Types and statuses to include; null means
    /// <see cref="MediaSearchOptions.Default"/> (karaoke, any status). Use
    /// <see cref="MediaSearchOptions.AllTypes"/> for everything.</param>
    Task<PaginatedResult<Media>> ReadAllAsync(int pageNumber, int pageSize, SortDescriptor? sort, MediaSearchOptions? options);

    /// <summary>Search with both a sort and a type/status filter.</summary>
    /// <param name="query">Text to match; null, empty or whitespace matches every row.</param>
    /// <param name="pageNumber">1-based page to read; below 1 reads page 1.</param>
    /// <param name="pageSize">Rows per page; below 1 uses the default, and it is capped (see <see cref="ISearchable{T}"/>).</param>
    /// <param name="sort">Null keeps relevance order where the query is ranked; a sort replaces
    /// relevance rather than breaking ties within it.</param>
    /// <param name="options">As for <see cref="ReadAllAsync(int, int, SortDescriptor?, MediaSearchOptions?)"/>.</param>
    Task<PaginatedResult<Media>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort, MediaSearchOptions? options);

    /// <summary>Every row of these types, unpaged; a paged picker would drop rows past page one.</summary>
    /// <returns>Ordered by title; empty when <paramref name="types"/> is empty.</returns>
    Task<IReadOnlyList<Media>> ReadAllByTypesAsync(params MediaType[] types);

    /// <summary>Dedup spans every type: FilePath is unique table-wide, ads included.</summary>
    /// <returns>The subset of <paramref name="filePaths"/> already in the library. Paths compare
    /// without regard to case, except on Linux, where case distinguishes two files.</returns>
    Task<HashSet<string>> GetExistingFilePathsAsync(IEnumerable<string> filePaths);

    /// <summary>Row whose FilePath matches, compared like <see cref="GetExistingFilePathsAsync"/>.</summary>
    /// <returns>The row, of any type, or null when no row has this path.</returns>
    Task<Media?> FindByFilePathAsync(string filePath);

    /// <summary>Rows whose size is one of <paramref name="sizes"/>: the dedup prefilter.</summary>
    Task<IReadOnlyList<Media>> GetByFileSizesAsync(IEnumerable<long> sizes);

    /// <summary>Rows imported before content dedup, which have no size to match on yet.</summary>
    Task<IReadOnlyList<Media>> GetWithoutFileSizeAsync();

    /// <summary>Persists size and hashes only, leaving every other column on the row untouched.</summary>
    Task UpdateFingerprintsAsync(IEnumerable<Media> media);
}
