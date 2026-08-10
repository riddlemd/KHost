using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface ILyricsService
{
    Task<Lyrics?> SearchAsync(string query, CancellationToken cancellationToken = default);
}
