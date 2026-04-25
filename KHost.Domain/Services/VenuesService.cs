using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services;

public class VenuesService : BaseRepositoryService<Venue, IVenuesRepository>, IVenuesService
{
    public Guid? SelectedVenueId { get; private set; }

    public IOptionsMonitor<ServiceOptions> Options { get; }

    public VenuesService(ILogger<VenuesService> logger, IOptionsMonitor<ServiceOptions> options, IVenuesRepository repository)
        : base(logger, repository)
    {
        Options = options;
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
        InvokeStateChanged();
        await Task.CompletedTask;
    }

    public class ServiceOptions
    {
        public const string SectionName = nameof(VenuesService);

    }
}
