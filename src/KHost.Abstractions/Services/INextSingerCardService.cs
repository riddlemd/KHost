using KHost.Abstractions.Services.IPC;

namespace KHost.Abstractions.Services;

/// <summary>Puts who is up on the screens, when a host asks for it.</summary>
/// <remarks>Nothing here is automatic. The card goes up because somebody pressed the button, and
/// comes down when the next thing is drawn, so there is no state to keep in step. Drawing it is the
/// display's business: this composes the card and hands it on.
///
/// <para>Host-owned; a plugin has nothing to implement. A display provider may call
/// <see cref="BuildAsync"/> for the card's content. A host singleton, callable from any
/// thread.</para></remarks>
public interface INextSingerCardService
{
    /// <summary>Who the card would name right now, or null when there is nobody to announce.</summary>
    /// <remarks>Skips the singer at the microphone. Names the singer as they asked to be called
    /// when the venue allows it, and their next queued song; a singer with nothing queued is named
    /// alone, with no song.</remarks>
    Task<ShowNextSingerCommand?> BuildAsync(CancellationToken cancellationToken = default);

    /// <summary>Puts the card up. False when the queue had nobody left to name.</summary>
    /// <remarks>Returns once the card has been handed on for drawing: it is published as
    /// <see cref="KHost.Abstractions.Messaging.Messages.NextSingerAnnounced"/> and awaited, so every
    /// display provider listening has had it by then.</remarks>
    Task<bool> AnnounceAsync(CancellationToken cancellationToken = default);
}
