using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

namespace KHost.Abstractions.Services;

/// <summary>The console's search box: routes a query to one <see cref="IMediaProvider"/>.</summary>
/// <remarks>Host-owned; a plugin supplies a source by implementing <see cref="IMediaProvider"/>, not
/// this. A host singleton, callable from any thread. Announces nothing. A provider that throws
/// contributes no rows rather than failing the search.</remarks>
public interface IMediaSearchService
{
    /// <summary>Every registered provider, host-built and plugin alike, in registration order.</summary>
    IReadOnlyList<IMediaProvider> Providers { get; }

    /// <summary>Searches the local library; remote providers cost a round trip, so ask explicitly.</summary>
    /// <returns>Empty when nothing matches, or when no local library provider is registered.</returns>
    Task<List<MediaSearchEntity>> SearchAsync(string query, int pageNumber = 0, int pageSize = 0);

    /// <summary><paramref name="source"/> is a provider's SourceName; an unknown one finds nothing.</summary>
    /// <remarks>One source per call, so rows from different providers never mix in one
    /// list.</remarks>
    Task<List<MediaSearchEntity>> SearchAsync(string query, string source, int pageNumber = 0, int pageSize = 0);
}
