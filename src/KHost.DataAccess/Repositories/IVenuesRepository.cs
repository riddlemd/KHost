using KHost.Abstractions.Models;

namespace KHost.DataAccess.Repositories;

/// <summary>Venues: the rooms a host runs shows in, each with its own settings.</summary>
/// <remarks>
/// <para>Host-implemented. A plugin goes through <see cref="KHost.Abstractions.Services.IVenuesService"/>, which
/// announces <see cref="KHost.Abstractions.Messaging.Messages.VenuesChanged"/> (and
/// <see cref="KHost.Abstractions.Messaging.Messages.SelectedVenueChanged"/> when the selected venue is edited),
/// refuses to delete the selected venue, and knows which venue is selected.</para>
/// <para>Search matches the venue name without regard to case or accents. Sort keys: <c>name</c>
/// (default), <c>enabled</c>.</para>
/// </remarks>
public interface IVenuesRepository : IRepository<Venue>, ISearchable<Venue>
{
}
