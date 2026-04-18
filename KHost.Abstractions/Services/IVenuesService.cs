using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IVenuesService
{
    event Action? StateChanged;

    IReadOnlyList<IVenue> Venues { get; }
    Guid? SelectedVenueId { get; }
    IVenue? SelectedVenue { get; }

    Task<IVenue> CreateAsync(string name);
    Task UpdateAsync(Guid venueId, string name, string notes = "", bool enabled = true);
    Task RemoveAsync(Guid venueId);
    Task<IPaginatedResult<IVenue>> SearchAsync(string query, int pageNumber = 1, int pageSize = 50);
    Task<IVenue?> ReadByIdAsync(Guid venueId);
    Task<IVenue?> ReadByNameAsync(string name);
    Task SelectVenueAsync(Guid? venueId);
}
