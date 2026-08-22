using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IPlaybackService : IDisposable
{
    event EventHandler? StateChanged;

    /// <summary>
    /// Raised by the position clock alone, twice a second while a song plays. Nothing but
    /// <see cref="Position"/> has moved, so a subscriber that re-queries on it repeats that query
    /// all night — take it only to redraw a playhead, and take <see cref="StateChanged"/> for the rest.
    /// </summary>
    event EventHandler? PositionChanged;

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

    /// <summary>
    /// Moves the playhead. Clamped to the song, so a click at either end of a progress bar is a
    /// position rather than an error.
    /// </summary>
    Task SeekAsync(TimeSpan position);
}

// Stopping is appended so the existing numeric values stay stable for telemetry.
public enum PlaybackState { Stopped, Playing, Paused, Stopping }
