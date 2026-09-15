using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

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
    /// <remarks>
    /// One source at a time, always. A search-everything method lived here and had no caller
    /// outside its own tests, while the single result set it could produce was the reason
    /// everything downstream had to cope with rows from several providers sharing one table — the
    /// headings, the queued badge, and anything else keyed on a row's source. A console asks one
    /// source at a time because that is what a host picked.
    /// </remarks>
    Task<List<MediaSearchEntity>> SearchAsync(string query, string source, int pageNumber = 0, int pageSize = 0);
}
