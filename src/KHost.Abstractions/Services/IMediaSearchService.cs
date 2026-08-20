using KHost.Plugins.Sdk.Models;
using KHost.Plugins.Sdk.Services;

namespace KHost.Abstractions.Services;

public interface IMediaSearchService
{
    /// <summary>Every registered provider, host-built and plugin alike, in registration order.</summary>
    IReadOnlyList<IMediaProvider> Providers { get; }

    Task<List<MediaSearchEntity>> SearchAsync(string query, int pageNumber = 0, int pageSize = 0);

    /// <summary><paramref name="source"/> is a provider's SourceName; an unknown one finds nothing.</summary>
    Task<List<MediaSearchEntity>> SearchAsync(string query, string source, int pageNumber = 0, int pageSize = 0);
}
