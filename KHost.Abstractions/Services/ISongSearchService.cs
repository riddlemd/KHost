using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface ISongSearchService
{
    Task<List<ISongSearchEntity>> SearchAsync(string directory, string? filter = null, bool recursive = true);
}
