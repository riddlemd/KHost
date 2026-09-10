using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IVenuesService : IRepositoryService<Venue>
{
    Guid? SelectedVenueId { get; }

    /// <summary>Restores the previously selected venue. Call once at startup.</summary>
    Task InitializeAsync();

    Task SelectVenueAsync(Guid? venueId);
    Task<Venue?> ReadSelectedVenueAsync();
}
