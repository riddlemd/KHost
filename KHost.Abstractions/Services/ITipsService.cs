namespace KHost.Abstractions.Services;

using KHost.Abstractions.Models;

public interface ITipsService : IRepositoryService<Tip>
{
    Task<IReadOnlyList<Tip>> GetByUserIdAsync(Guid userId);
    Task<decimal> GetTotalByUserIdAsync(Guid userId, DateTime? from = null, DateTime? to = null);
}
