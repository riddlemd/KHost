using KHost.Abstractions.Models;
using KHost.Plugins.Sdk.Models;

namespace KHost.Abstractions.Services;

public interface ISingerQueueService
{
    event EventHandler? StateChanged;

    IReadOnlyList<KHostUser> Users { get; }
    Guid? SelectedUserId { get; }
    KHostUser? SelectedUser { get; }
    bool IsTopSlotLocked { get; }

    Task InitializeAsync();
    Task SelectUserAsync(Guid? userId);
    Task AddUserAsync(Guid userId);
    Task RemoveUserAsync(Guid userId);
    Task AddMediaAsync(Guid userId, MediaSearchEntity media);
    Task MoveUserUpAsync(Guid userId);
    Task MoveUserDownAsync(Guid userId);
    Task MoveUserToStartAsync(Guid userId);
    Task MoveUserToEndAsync(Guid userId);
    Task MoveUserToIndexAsync(Guid userId, int newIndex);
    /// <summary>Reorders the queue after a performance using the venue's rotation config (fifo drop-to-end by default).</summary>
    Task RotateQueueAsync(Guid finishedSingerId);
    Task SelectFirstUserInQueueAsync();
    Task RefreshAsync();
    Task ClearAsync();
    void LockTopSlot();
    void UnlockTopSlot();
}
