using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>A place songs can be searched for and acted on from the console, such as the library or
/// an online catalog.</summary>
/// <remarks>An extension point: a plugin IMPLEMENTS it and the host discovers it, listing it as
/// "Media provider" on the Plugins page and offering it as a search source. The host ships one
/// itself for the local library. A provider hands back rows to show and the actions on them; what
/// an action does — download, enqueue, sign in — is the provider's, and it reaches the host through
/// the services it takes in its constructor (<see cref="IMediaAcquisitionService"/>,
/// <see cref="IPerformanceService.CreateAndEnqueueAsync"/>).
///
/// <para>The plugin's object is one singleton shared across every extension interface it
/// implements, and is called from any thread.</para></remarks>
public interface IMediaProvider
{
    /// <summary>What the console calls this source.</summary>
    string DisplayName { get; }

    /// <summary>Stable key naming this source; a search asks for it by this.</summary>
    /// <remarks>Matched exactly, with case. Two providers sharing one are both asked and their rows
    /// merged.</remarks>
    string SourceName { get; }

    /// <summary>The actions offered on this provider's results.</summary>
    IEnumerable<MediaProviderAction> Actions { get; }

    /// <summary>Columns this provider's results show, left to right; empty takes the default.</summary>
    /// <remarks>The console owns the rest: the queued badge is first, actions are last. Has a default
    /// body returning empty.</remarks>
    IReadOnlyList<MediaResultColumn> Columns => [];

    /// <summary>The rows matching <paramref name="query"/>.</summary>
    /// <param name="query">What the host typed, as typed.</param>
    /// <param name="pageNumber">1-based page; zero or less means the first.</param>
    /// <param name="pageSize">Rows per page; zero or less means the provider's own default.</param>
    /// <returns>Never null; empty for no match.</returns>
    /// <remarks>Asked only when a host searches this source by name, so a remote round trip is
    /// acceptable here. A throw is logged and treated as no results.</remarks>
    Task<List<MediaSearchEntity>> SearchAsync(string query, int pageNumber = 0, int pageSize = 0);
}
