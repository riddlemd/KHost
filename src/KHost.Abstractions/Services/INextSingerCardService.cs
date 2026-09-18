using KHost.Abstractions.Services.IPC;

namespace KHost.Abstractions.Services;

/// <summary>Puts who is up on the screens, when a host asks for it.</summary>
/// <remarks>Nothing here is automatic. The card goes up because somebody pressed the button, and
/// comes down when the next thing is drawn, so there is no state to keep in step.</remarks>
public interface INextSingerCardService
{
    /// <summary>Who the card would name right now, or null when there is nobody to announce.</summary>
    Task<ShowNextSingerCommand?> BuildAsync(CancellationToken cancellationToken = default);

    /// <summary>Puts the card up. False when the queue had nobody left to name.</summary>
    Task<bool> AnnounceAsync(CancellationToken cancellationToken = default);
}
