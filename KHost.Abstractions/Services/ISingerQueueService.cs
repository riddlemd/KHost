using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface ISingerQueueService
{
    event EventHandler? StateChanged;

    IReadOnlyList<Singer> Singers { get; }
    Guid? SelectedSingerId { get; }
    Singer? SelectedSinger { get; }
    bool IsTopSlotLocked { get; }

    Task SelectSingerAsync(Guid? singerId);
    Task AddSingerAsync(Guid singerId);
    Task RemoveSingerAsync(Guid singerId);
    Task AddMediaAsync(Guid singerId, MediaSearchEntity media);
    Task MoveSingerUpAsync(Guid singerId);
    Task MoveSingerDownAsync(Guid singerId);
    Task MoveSingerToStartAsync(Guid singerId);
    Task MoveSingerToEndAsync(Guid singerId);
    Task MoveSingerToIndexAsync(Guid singerId, int newIndex);
    Task SelectFirstSingerInQueueAsync();
    Task RefreshAsync();
    Task ClearAsync();
    void LockTopSlot();
    void UnlockTopSlot();
}
