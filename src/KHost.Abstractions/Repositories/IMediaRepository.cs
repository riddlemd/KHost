using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

public interface IMediaRepository : IRepository<Media>
{
    Task<HashSet<string>> GetExistingFilePathsAsync(IEnumerable<string> filePaths);

    /// <summary>Rows whose file size is one of <paramref name="sizes"/> — the prefilter for content dedup.</summary>
    Task<IReadOnlyList<Media>> GetByFileSizesAsync(IEnumerable<long> sizes);

    /// <summary>Rows imported before content dedup, which have no size to match on yet.</summary>
    Task<IReadOnlyList<Media>> GetWithoutFileSizeAsync();

    /// <summary>Persists size and hashes only, leaving every other column on the row untouched.</summary>
    Task UpdateFingerprintsAsync(IEnumerable<Media> media);
}
