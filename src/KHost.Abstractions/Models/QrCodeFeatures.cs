namespace KHost.Abstractions.Models;

/// <summary>What a guest can do once they scan a QR code, as its owner declares it.</summary>
/// <remarks>The host shows a declared code only while the venue offers at least one of these, so a
/// code for sign-ups alone goes down when the venue stops taking them, while one that also takes
/// tips stays up. <see cref="None"/> declares nothing and is always shown.</remarks>
[Flags]
public enum QrCodeFeatures
{
    /// <summary>Nothing declared: shown whenever the venue picks this owner, whatever is open.</summary>
    None = 0,

    /// <summary>Signing up for a song. Open while <see cref="Venue.VenueSettings.AllowGuestRemote"/>.</summary>
    SongEnqueue = 1,

    /// <summary>Seeing the queue. Open while <see cref="Venue.VenueSettings.ShowQueueToGuests"/>.</summary>
    QueueView = 2,

    /// <summary>Changing the guest's own songs already queued. No venue setting closes it yet.</summary>
    QueueEdit = 4,

    /// <summary>Tipping. Open while <see cref="Venue.VenueSettings.TippingEnabled"/>.</summary>
    Tipping = 8,
}
