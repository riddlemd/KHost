using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

public interface IMediaRepository : IRepository<Media>
{
    /// <summary>
    /// Listing and search that can reach past karaoke. The inherited overloads answer with songs
    /// alone; break music and ads are only visible to a caller that asks for them by name.
    /// </summary>
    Task<PaginatedResult<Media>> ReadAllAsync(int pageNumber, int pageSize, SortDescriptor? sort, MediaSearchOptions? options);

    Task<PaginatedResult<Media>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort, MediaSearchOptions? options);

    /// <summary>
    /// Dedup reads deliberately span every kind: FilePath is unique across the table, so an ad
    /// already imported has to be found before the same path is inserted again as a song.
    /// </summary>
    Task<HashSet<string>> GetExistingFilePathsAsync(IEnumerable<string> filePaths);

    /// <summary>Row whose FilePath matches, under the same case-folding rules as <see cref="GetExistingFilePathsAsync"/>.</summary>
    Task<Media?> FindByFilePathAsync(string filePath);

    /// <summary>Rows whose file size is one of <paramref name="sizes"/> — the prefilter for content dedup.</summary>
    Task<IReadOnlyList<Media>> GetByFileSizesAsync(IEnumerable<long> sizes);

    /// <summary>Rows imported before content dedup, which have no size to match on yet.</summary>
    Task<IReadOnlyList<Media>> GetWithoutFileSizeAsync();

    /// <summary>Persists size and hashes only, leaving every other column on the row untouched.</summary>
    Task UpdateFingerprintsAsync(IEnumerable<Media> media);
}
