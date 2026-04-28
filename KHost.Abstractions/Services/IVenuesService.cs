using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IVenuesService : IRepositoryService<Venue>
{
    Guid? SelectedVenueId { get; }

    Task SelectVenueAsync(Guid? guid);
    Task<Venue?> ReadSelectedVenueAsync();
}
