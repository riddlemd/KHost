using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IPlaybackService : IDisposable
{
    event EventHandler? StateChanged;

    Performance? CurrentPerformance { get; }
    Media? CurrentMedia { get; }
    PlaybackState State { get; }
    TimeSpan Position { get; }
    Guid? CurrentlyPerformingUserId { get; }

    /// <summary>How long the current stop is fading out for; null when not stopping.</summary>
    TimeSpan? StopFadeDuration { get; }

    /// <summary>
    /// Whether at least one screen is connected. Screens render both audio and video, so
    /// playback with none attached produces no output at all.
    /// </summary>
    Task<bool> HasConnectedScreenAsync();

    Task LoadAsync(Performance performance, Media media);
    Task PlayAsync();
    Task PauseAsync();
    Task StopAsync();
}

// Stopping is appended so the existing numeric values stay stable for telemetry.
public enum PlaybackState { Stopped, Playing, Paused, Stopping }
