using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Puts who is up on the screens, when a host asks for it, and takes it down again.</summary>
/// <remarks>Nothing here is automatic. The card goes up because somebody pressed the button, and
/// comes down when they press it again or when the next thing is drawn over it. Drawing it is the
/// display's business: this composes the card, hands it on, and keeps whether it is up.
///
/// <para>Host-owned; a plugin has nothing to implement. A display provider may call
/// <see cref="BuildAsync"/> for the card's content. A host singleton, callable from any
/// thread.</para></remarks>
public interface INextSingerCardService
{
    /// <summary>Whether the card is up.</summary>
    /// <remarks>True from an <see cref="AnnounceAsync"/> that returned true until
    /// <see cref="WithdrawAsync"/>, or until the program moves or the selected venue changes while
    /// nothing is on, either of which draws over the card. Every change is followed by
    /// <see cref="KHost.Abstractions.Messaging.Messages.NextSingerAnnounced"/> or
    /// <see cref="KHost.Abstractions.Messaging.Messages.NextSingerWithdrawn"/>, by which time it
    /// already reads the new value.</remarks>
    bool IsAnnounced { get; }

    /// <summary>Who the card would name right now, or null when there is nobody to announce.</summary>
    /// <remarks>Skips the singer at the microphone. Names the singer as they asked to be called
    /// when the venue allows it, and their next queued song; a singer with nothing queued is named
    /// alone, with no song.</remarks>
    Task<NextSingerCard?> BuildAsync(CancellationToken cancellationToken = default);

    /// <summary>Puts the card up. False when the queue had nobody left to name.</summary>
    /// <remarks>Returns once the card has been handed on for drawing: it is published as
    /// <see cref="KHost.Abstractions.Messaging.Messages.NextSingerAnnounced"/> and awaited, so every
    /// display provider listening has had it by then.</remarks>
    Task<bool> AnnounceAsync(CancellationToken cancellationToken = default);

    /// <summary>Takes the card down. Does nothing when it is not up.</summary>
    /// <remarks>Returns once <see cref="KHost.Abstractions.Messaging.Messages.NextSingerWithdrawn"/>
    /// has been published and awaited, so every display provider listening has had it by then.</remarks>
    Task WithdrawAsync(CancellationToken cancellationToken = default);
}
