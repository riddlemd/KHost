namespace KHost.Domain.Services;

using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

public class TipsService : BaseRepositoryService<Tip, ITipsRepository>, ITipsService
{
    public TipsService(ILogger<TipsService> logger, ITipsRepository repository)
        : base(logger, repository)
    {
    }

    public Task<IReadOnlyList<Tip>> GetByUserIdAsync(Guid userId)
        => Repository.GetByUserIdAsync(userId);

    public Task<decimal> GetTotalByUserIdAsync(Guid userId, DateTime? from = null, DateTime? to = null)
        => Repository.GetTotalByUserIdAsync(userId, from, to);
}
