using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IPlaybackService : IDisposable
{
    event EventHandler? StateChanged;

    Performance? CurrentPerformance { get; }
    Media? CurrentMedia { get; }
    PlaybackState State { get; }
    TimeSpan Position { get; }
    Guid? CurrentlyPerformingSingerId { get; }

    Task LoadAsync(Performance performance, Media media);
    Task PlayAsync();
    Task PauseAsync();
    Task StopAsync();
}

public enum PlaybackState { Stopped, Playing, Paused }
