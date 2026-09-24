using KHost.Abstractions.Models.Backgrounds;

namespace KHost.Abstractions.Services;

/// <summary>The backgrounds a song may be rendered against.</summary>
/// <remarks>Two flat folders of clips, each optionally with a still of the same name beside it: the
/// set shipped with the app, and one the host points at. A venue chooses from what they hold
/// between them; it does not say where they are.
///
/// <para>Host-owned; a plugin may take it to read the list but has nothing to implement. A host
/// singleton, callable from any thread. Announces nothing.</para></remarks>
public interface IBackgroundPackService
{
    /// <summary>Everything on offer, or why there is nothing.</summary>
    /// <remarks>Never throws for a folder that is absent, unreadable or nonsense. A background is
    /// decoration, and a song still has to reach the microphone when the decoration is missing.
    /// Reflects the folders as they are at the call. A clip in the host's folder shadows a shipped
    /// one of the same file name, and entries come back ordered by file name.</remarks>
    Task<BackgroundPack> ReadAsync(CancellationToken cancellationToken = default);
}
