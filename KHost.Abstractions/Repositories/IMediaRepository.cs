using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

public interface IMediaRepository : IRepository<Media>
{
    Task<HashSet<string>> GetExistingFilePathsAsync(IEnumerable<string> filePaths);
}
