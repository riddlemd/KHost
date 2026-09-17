using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

namespace KHost.Abstractions.Services;

public interface IMediaSearchService
{
    /// <summary>Every registered provider, host-built and plugin alike, in registration order.</summary>
    IReadOnlyList<IMediaProvider> Providers { get; }

    /// <summary>Searches the local library; remote providers cost a round trip, so ask explicitly.</summary>
    Task<List<MediaSearchEntity>> SearchAsync(string query, int pageNumber = 0, int pageSize = 0);

    /// <summary><paramref name="source"/> is a provider's SourceName; an unknown one finds nothing.</summary>
    /// <remarks>One source at a time; search-everything mixed rows from several providers.</remarks>
    Task<List<MediaSearchEntity>> SearchAsync(string query, string source, int pageNumber = 0, int pageSize = 0);
}
