using KHost.LrcLib.Models;

namespace KHost.LrcLib;

public interface ILrcLibClient
{
    Task<LyricsRecord?> GetAsync(GetLyricsRequest request, CancellationToken cancellationToken = default);

    Task<LyricsRecord?> GetCachedAsync(GetLyricsRequest request, CancellationToken cancellationToken = default);

    Task<LyricsRecord?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LyricsRecord>> SearchAsync(SearchLyricsRequest request, CancellationToken cancellationToken = default);
}
