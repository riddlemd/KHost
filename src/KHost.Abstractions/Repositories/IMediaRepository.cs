using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

public interface IMediaRepository : IRepository<Media>
{
    /// <summary>Listing and search that reach past karaoke; the plain overloads answer songs alone.</summary>
    Task<PaginatedResult<Media>> ReadAllAsync(int pageNumber, int pageSize, SortDescriptor? sort, MediaSearchOptions? options);

    Task<PaginatedResult<Media>> SearchAsync(string query, int pageNumber, int pageSize, SortDescriptor? sort, MediaSearchOptions? options);

    /// <summary>Every row of these types, unpaged; a paged picker would drop rows past page one.</summary>
    Task<IReadOnlyList<Media>> ReadAllByTypesAsync(params MediaType[] types);

    /// <summary>Dedup spans every type: FilePath is unique table-wide, ads included.</summary>
    Task<HashSet<string>> GetExistingFilePathsAsync(IEnumerable<string> filePaths);

    /// <summary>Row whose FilePath matches, folded like <see cref="GetExistingFilePathsAsync"/>.</summary>
    Task<Media?> FindByFilePathAsync(string filePath);

    /// <summary>Rows whose size is one of <paramref name="sizes"/>: the dedup prefilter.</summary>
    Task<IReadOnlyList<Media>> GetByFileSizesAsync(IEnumerable<long> sizes);

    /// <summary>Rows imported before content dedup, which have no size to match on yet.</summary>
    Task<IReadOnlyList<Media>> GetWithoutFileSizeAsync();

    /// <summary>Persists size and hashes only, leaving every other column on the row untouched.</summary>
    Task UpdateFingerprintsAsync(IEnumerable<Media> media);
}
