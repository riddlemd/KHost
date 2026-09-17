using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IMediaProvider
{
    string DisplayName { get; }
    string SourceName { get; }
    IEnumerable<MediaProviderAction> Actions { get; }

    /// <summary>Columns this provider's results show, left to right; empty takes the default.</summary>
    /// <remarks>The console owns the rest: the queued badge is first, actions are last.</remarks>
    IReadOnlyList<MediaResultColumn> Columns => [];
    Task<List<MediaSearchEntity>> SearchAsync(string query, int pageNumber = 0, int pageSize = 0);
}
