using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services;

public class VenuesService : BaseRepositoryService<Venue, IVenuesRepository>, IVenuesService
{
    private const string _cacheKey = "selected-venue";

    private readonly ICacheService _cacheService;

    public Guid? SelectedVenueId { get; private set; }

    public IOptionsMonitor<ServiceOptions> Options { get; }

    public VenuesService(ILogger<VenuesService> logger, IOptionsMonitor<ServiceOptions> options, IVenuesRepository repository, ICacheService cacheService)
        : base(logger, repository)
    {
        Options = options;
        _cacheService = cacheService;
    }

    public async Task InitializeAsync()
    {
        var cached = await _cacheService.LoadAsync<Guid?>(_cacheKey);

        if (cached is not { } id)
        {
            Logger.LogInformation("No previously selected venue; falling back to the first enabled venue");
            await SelectFirstAvailableVenueAsync();
            return;
        }

        // The venue may have been deleted since it was cached.
        if (await ReadAsync(id) is null)
        {
            Logger.LogWarning("Cached venue {VenueId} no longer exists; falling back", id);
            await SelectFirstAvailableVenueAsync();
            return;
        }

        SelectedVenueId = id;
        Logger.LogInformation("Selected venue restored: {VenueId}", id);
        InvokeStateChanged();
    }

    public async Task<Venue?> ReadSelectedVenueAsync()
    {
        if (SelectedVenueId is not { } id)
            return null;

        return await ReadAsync(id);
    }

    public async Task<Venue?> ReadByNameAsync(string name)
    {
        var result = await ReadAllAsync(pageNumber: 1, pageSize: 1000);
        return result.Items.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SelectVenueAsync(Guid? venueId)
    {
        SelectedVenueId = venueId;
        Logger.LogInformation("Venue selected: {VenueId}", venueId);

        await _cacheService.SaveAsync(_cacheKey, venueId);

        InvokeStateChanged();
    }
    
    public override async Task<bool> DeleteAsync(Guid id)
    {
        if (SelectedVenueId == id)
        {
            Logger.LogWarning("Refused to delete venue {VenueId} because it is currently selected", id);
            return false;
        }

        return await base.DeleteAsync(id);
    }

    private async Task SelectFirstAvailableVenueAsync()
    {
        var venues = await ReadAllAsync(pageNumber: 1, pageSize: 1000);
        var first = venues.Items.FirstOrDefault(v => v.Enabled) ?? venues.Items.FirstOrDefault();

        await SelectVenueAsync(first?.Id);
    }

    public class ServiceOptions
    {
        public const string SectionName = nameof(VenuesService);

    }
}
