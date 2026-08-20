namespace KHost.Abstractions.Repositories;

using KHost.Abstractions.Models;

public interface ITipsRepository : IRepository<Tip>
{
    Task<IReadOnlyList<Tip>> GetByUserIdAsync(Guid userId);
    /// <summary>Total in whole cents.</summary>
    Task<int> GetTotalByUserIdAsync(Guid userId, DateTime? from = null, DateTime? to = null);
}
