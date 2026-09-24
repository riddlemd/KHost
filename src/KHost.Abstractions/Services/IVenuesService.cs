using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>The venues a host plays at, and which one tonight's show is running as.</summary>
/// <remarks>The selected venue's settings steer most of the show: its volume, break music, ads,
/// marquee, QR source and rotation rules. A plugin may take it to read them. The selection survives
/// a restart. The selected venue cannot be deleted: <see cref="IRepositoryService{T}.DeleteAsync"/>
/// answers false for it.
///
/// <para>A host singleton, callable from any thread. Every create, update and delete announces
/// <see cref="KHost.Abstractions.Messaging.Messages.VenuesChanged"/>. Selecting a venue, restoring
/// the selection at startup, or updating the selected venue also announces
/// <see cref="KHost.Abstractions.Messaging.Messages.SelectedVenueChanged"/>, which is the one to
/// follow for anything the room hears or sees.</para></remarks>
public interface IVenuesService : IRepositoryService<Venue>
{
    /// <summary>The venue the show is running as, or null when none is selected.</summary>
    Guid? SelectedVenueId { get; }

    /// <summary>Restores the previously selected venue. Call once at startup.</summary>
    /// <remarks>Falls back to the first enabled venue, else the first of any, when nothing was saved
    /// or the saved venue has been deleted. Host-called; a plugin should not call it.</remarks>
    Task InitializeAsync();

    /// <summary>Makes <paramref name="venueId"/> the show's venue, or selects none with null.</summary>
    /// <remarks>Not checked against the stored venues.</remarks>
    Task SelectVenueAsync(Guid? venueId);

    /// <summary>The selected venue as stored now, or null when none is selected or it no longer
    /// exists.</summary>
    Task<Venue?> ReadSelectedVenueAsync();
}
