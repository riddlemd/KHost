using KHost.Abstractions.Models.Backgrounds;

namespace KHost.Abstractions.Services;

/// <summary>The backgrounds a song may be rendered against.</summary>
/// <remarks>Two flat folders of clips, each optionally with a still of the same name beside it: the
/// set shipped with the app, and one the host points at. A venue chooses from what they hold
/// between them; it does not say where they are.</remarks>
public interface IBackgroundPackService
{
    /// <summary>Everything on offer, or why there is nothing.</summary>
    /// <remarks>Never throws for a folder that is absent, unreadable or nonsense. A background is
    /// decoration, and a song still has to reach the microphone when the decoration is missing.
    /// </remarks>
    Task<BackgroundPack> ReadAsync(CancellationToken cancellationToken = default);
}
