using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface ISingerQueueService
{
    event Action? StateChanged;

    IReadOnlyList<ISinger> Singers { get; }
    Guid? SelectedSingerId { get; }
    ISinger? SelectedSinger { get; }
    Guid? SelectedQueuedSongId { get; }
    IQueuedSong? SelectedQueuedSong { get; }
    ISinger? CurrentlyPerformingSinger { get; }

    Task SelectSingerAsync(Guid? singerId);
    Task SelectSongAsync(Guid? queuedSongId);
    Task<ISinger> AddSingerAsync(string name);
    Task RemoveSingerAsync(Guid singerId);
    Task AddSongAsync(Guid singerId, ISongSearchEntity song);
    Task RemoveQueuedSongAsync(Guid singerId, Guid queuedSongId);
    Task MoveSingerUpAsync(Guid singerId);
    Task MoveSingerDownAsync(Guid singerId);
    Task MoveSingerToStartAsync(Guid singerId);
    Task MoveSingerToEndAsync(Guid singerId);
    Task SelectFirstSingerInQueueAsync();
    Task MoveQueuedSongUpAsync(Guid singerId, Guid queuedSongId);
    Task MoveQueuedSongDownAsync(Guid singerId, Guid queuedSongId);
    Task MoveQueuedSongToEndAsync(Guid singerId, Guid queuedSongId);
    Task ToggleSingerIsRegularAsync(Guid singerId);
    Task ToggleSingerIsTipperAsync(Guid singerId);
}
