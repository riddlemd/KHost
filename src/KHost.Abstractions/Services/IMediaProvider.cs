using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IMediaProvider
{
    string DisplayName { get; }
    string SourceName { get; }
    IEnumerable<MediaProviderAction> Actions { get; }

    /// <summary>
    /// The columns this provider's results are shown in, left to right. Empty takes the console's
    /// default title/artist/length — right for a library that stores those three apart, and wrong
    /// for a source whose answer to "which of these do I want" is something else entirely.
    /// </summary>
    /// <remarks>
    /// The console owns the row beyond these: the "already queued" badge rides the first column,
    /// and the actions are appended after the last.
    /// </remarks>
    IReadOnlyList<MediaResultColumn> Columns => [];
    Task<List<MediaSearchEntity>> SearchAsync(string query, int pageNumber = 0, int pageSize = 0);
}
