using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IMediaSearchService
{
    Task<List<MediaSearchEntity>> SearchAsync(string query, int pageNumber = 0, int pageSize = 0);
}
