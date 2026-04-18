using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IPlaybackService : IDisposable
{
    event Action? StateChanged;

    IQueuedSong? CurrentQueuedSong { get; }
    PlaybackState State { get; }
    TimeSpan Position { get; }

    void Load(IQueuedSong song);
    Task PlayAsync();
    void Pause();
    Task StopAsync();
}

public enum PlaybackState { Stopped, Playing, Paused }
