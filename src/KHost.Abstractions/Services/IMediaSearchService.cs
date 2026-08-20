using KHost.Plugins.Sdk.Models;
using KHost.Plugins.Sdk.Services;

namespace KHost.Abstractions.Services;

public interface IMediaSearchService
{
    /// <summary>Every registered provider, host-built and plugin alike, in registration order.</summary>
    IReadOnlyList<IMediaProvider> Providers { get; }

    /// <summary>
    /// Searches the local library only. Remote providers cost a network round trip each and are
    /// rate-limited or metered, so reaching them is always something the host asks for explicitly
    /// rather than the price of every search.
    /// </summary>
    Task<List<MediaSearchEntity>> SearchAsync(string query, int pageNumber = 0, int pageSize = 0);

    /// <summary><paramref name="source"/> is a provider's SourceName; an unknown one finds nothing.</summary>
    Task<List<MediaSearchEntity>> SearchAsync(string query, string source, int pageNumber = 0, int pageSize = 0);

    /// <summary>Searches every registered provider at once. Only call this when the host asked for it.</summary>
    Task<List<MediaSearchEntity>> SearchAllAsync(string query, int pageNumber = 0, int pageSize = 0);
}
