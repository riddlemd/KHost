using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

/// <summary>Venues: the rooms a host runs shows in, each with its own settings.</summary>
/// <remarks>
/// <para>Host-implemented. A plugin goes through <see cref="Services.IVenuesService"/>, which
/// announces <see cref="Messaging.Messages.VenuesChanged"/> (and
/// <see cref="Messaging.Messages.SelectedVenueChanged"/> when the selected venue is edited),
/// refuses to delete the selected venue, and knows which venue is selected.</para>
/// <para>Search matches the venue name without regard to case or accents. Sort keys: <c>name</c>
/// (default), <c>enabled</c>.</para>
/// </remarks>
public interface IVenuesRepository : IRepository<Venue>
{
}
