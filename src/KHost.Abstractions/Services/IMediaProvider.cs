using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IMediaProvider
{
    string DisplayName { get; }
    string SourceName { get; }
    IEnumerable<MediaProviderAction> Actions { get; }
    Task<List<MediaSearchEntity>> SearchAsync(string query, int pageNumber = 0, int pageSize = 0);
}
