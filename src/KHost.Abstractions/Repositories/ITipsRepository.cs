namespace KHost.Abstractions.Repositories;

using KHost.Abstractions.Models;

/// <summary>Tips recorded against singers.</summary>
/// <remarks>
/// <para>Host-implemented. A plugin goes through <see cref="Services.ITipsService"/>, which
/// announces <see cref="Messaging.Messages.TipsChanged"/>, refuses a tip with no singer, and stamps
/// the selected venue on a new tip that names none.</para>
/// <para>Search matches the tip's notes, case- and accent-insensitive. Sort keys:
/// <c>createdDate</c> (default, newest first), <c>amount</c>, <c>paymentMethod</c>.</para>
/// </remarks>
public interface ITipsRepository : IRepository<Tip>
{
    /// <summary>Every tip from one singer, newest first; empty when there are none.</summary>
    Task<IReadOnlyList<Tip>> GetByUserIdAsync(Guid userId);

    /// <summary>Total in whole cents.</summary>
    /// <param name="userId">The singer whose tips are summed.</param>
    /// <param name="from">UTC, inclusive; null for no lower bound.</param>
    /// <param name="to">UTC, inclusive; null for no upper bound.</param>
    /// <returns>Zero when the singer has no tips in range.</returns>
    Task<int> GetTotalInCentsByUserIdAsync(Guid userId, DateTime? from = null, DateTime? to = null);
}
