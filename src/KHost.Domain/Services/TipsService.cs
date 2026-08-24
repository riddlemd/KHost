namespace KHost.Domain.Services;

using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using Microsoft.Extensions.Logging;

public class TipsService : BaseRepositoryService<Tip, ITipsRepository>, ITipsService
{
    private readonly IVenuesService _venuesService;

    protected override object? StateChangedMessage => new TipsChanged();

    public TipsService(ILogger<TipsService> logger, ITipsRepository repository, IVenuesService venuesService, IMessageBroker broker)
        : base(logger, repository, broker)
    {
        _venuesService = venuesService;
    }

    // Stamped at creation rather than read live: a tip belongs to the venue it was taken at, and
    // that must not move when the host switches venue later.
    public override async Task<Tip> CreateAsync(Tip entity)
    {
        EnsureSinger(entity);

        entity.VenueId ??= _venuesService.SelectedVenueId;

        return await base.CreateAsync(entity);
    }

    public override async Task UpdateAsync(Tip entity)
    {
        EnsureSinger(entity);

        await base.UpdateAsync(entity);
    }

    public Task<IReadOnlyList<Tip>> GetByUserIdAsync(Guid userId)
        => Repository.GetByUserIdAsync(userId);

    public Task<int> GetTotalInCentsByUserIdAsync(Guid userId, DateTime? from = null, DateTime? to = null)
        => Repository.GetTotalInCentsByUserIdAsync(userId, from, to);

    // Every view that reads tips reaches them through a singer, so one without is money recorded
    // where nothing can show it.
    private static void EnsureSinger(Tip entity)
    {
        if (entity.UserId == Guid.Empty)
            throw new ArgumentException("A tip must belong to a singer.", nameof(entity));
    }
}
