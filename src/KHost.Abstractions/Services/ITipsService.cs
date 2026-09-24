namespace KHost.Abstractions.Services;

using KHost.Abstractions.Models;

/// <summary>Tips singers have given, recorded against the singer and the venue they were taken at.
/// </summary>
/// <remarks>A plugin may take it, for instance to record a tip taken through a payment service. A
/// tip must name a singer: creating or updating one without throws
/// <see cref="ArgumentException"/>. A new tip with no venue is stamped with the selected one. A host
/// singleton, callable from any thread. Every create, update and delete announces
/// <see cref="KHost.Abstractions.Messaging.Messages.TipsChanged"/>.</remarks>
public interface ITipsService : IRepositoryService<Tip>
{
    /// <summary>Every tip from one singer, newest first.</summary>
    Task<IReadOnlyList<Tip>> GetByUserIdAsync(Guid userId);

    /// <summary>The sum of one singer's tips, in cents.</summary>
    /// <param name="userId">The singer whose tips are summed.</param>
    /// <param name="from">When given, only tips created at or after it, in UTC.</param>
    /// <param name="to">When given, only tips created at or before it, in UTC.</param>
    /// <returns>Zero when there are none.</returns>
    Task<int> GetTotalInCentsByUserIdAsync(Guid userId, DateTime? from = null, DateTime? to = null);
}
