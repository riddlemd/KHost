namespace KHost.Domain.Services;

using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

public class TipsService : BaseRepositoryService<Tip, ITipsRepository>, ITipsService
{
    private readonly IVenuesService _venuesService;

    public TipsService(ILogger<TipsService> logger, ITipsRepository repository, IVenuesService venuesService)
        : base(logger, repository)
    {
        _venuesService = venuesService;
    }

    // Stamped at creation rather than read live: a tip belongs to the venue it was taken at, and
    // that must not move when the host switches venue later.
    public override async Task<Tip> CreateAsync(Tip entity)
    {
        entity.VenueId ??= _venuesService.SelectedVenueId;

        return await base.CreateAsync(entity);
    }

    public Task<IReadOnlyList<Tip>> GetByUserIdAsync(Guid userId)
        => Repository.GetByUserIdAsync(userId);

    public Task<decimal> GetTotalByUserIdAsync(Guid userId, DateTime? from = null, DateTime? to = null)
        => Repository.GetTotalByUserIdAsync(userId, from, to);
}
