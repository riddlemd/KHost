namespace KHost.Abstractions.Services.QueueRotation;

public interface IQueueRotationStateService
{
    Task InitializeAsync();
    Task<IReadOnlyDictionary<Guid, int>> GetSongsSungTonightAsync(IEnumerable<Guid> userIds);
    Task<IReadOnlyDictionary<Guid, int>> GetMissedCallsAsync();
    Task IncrementMissedCallsAsync(Guid userId);
    Task ResetMissedCallsAsync(Guid userId);
    Task ClearAsync();
}
