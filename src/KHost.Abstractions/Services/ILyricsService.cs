using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Looks up a song's plain-text words online, for a host to read along.</summary>
/// <remarks>Untimed text for the console, not the timed words a display draws over a song; those come
/// from <see cref="ITimedLyricsService"/>. Each call goes out over the network. A host singleton,
/// callable from any thread. Announces nothing.</remarks>
public interface ILyricsService
{
    /// <summary>The best match for <paramref name="query"/>, typically a title and artist.</summary>
    /// <returns>Null when the query is blank, nothing matched, or the lookup service could not be
    /// reached.</returns>
    Task<Lyrics?> SearchAsync(string query, CancellationToken cancellationToken = default);
}
