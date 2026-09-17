using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Gap after a performance; handlers can't await, so filling work registers here instead.</summary>
public sealed class PerformanceEndedEventArgs : EventArgs
{
    private readonly List<Task> _fills = [];

    public void Fill(Task work) => _fills.Add(work);

    public Task WhenFilledAsync() => _fills.Count == 0 ? Task.CompletedTask : Task.WhenAll(_fills);
}

public interface IPlaybackService : IDisposable
{
    /// <summary>Semitones; past this, WSOLA artefacts cost more than the transposition is worth.</summary>
    const int MaxPitch = 6;

    const int MinPitch = -6;

    /// <summary>Percent either side of recorded speed; past this the stretch smears too badly.</summary>
    const int MaxTempo = 50;

    const int MinTempo = -50;


    /// <summary>A performance finished, raised before break music returns; never raised for an ad.</summary>
    event EventHandler<PerformanceEndedEventArgs>? PerformanceEnded;

    /// <summary>Twice a second while playing; only <see cref="Position"/> moves, so redraw only.</summary>
    event EventHandler? PositionChanged;

    Performance? CurrentPerformance { get; }
    Media? CurrentMedia { get; }

    /// <summary>Who's singing; the venue decides if a differing recorded name shows. Null if idle.</summary>
    string? CurrentSingerName { get; }

    /// <summary>Whether the main channel is carrying an ad rather than a singer's song.</summary>
    bool IsPlayingAd { get; }
    PlaybackState State { get; }
    TimeSpan Position { get; }
    Guid? CurrentlyPerformingUserId { get; }

    /// <summary>How long the current stop is fading out for; null when not stopping.</summary>
    TimeSpan? StopFadeDuration { get; }

    /// <summary>Semitones from the performance on load; applied in the transcode, not sent out.</summary>
    int Pitch { get; }

    /// <summary>Percent either side of recorded speed; retimes the picture with the audio.</summary>
    int Tempo { get; }

    /// <summary>Whether a screen is connected; with none, playback produces no output.</summary>
    Task<bool> HasConnectedScreenAsync();

    Task LoadAsync(Performance performance, Media media);

    /// <summary>Plays media on nobody's turn; ends without dequeuing. False if refused or nowhere.</summary>
    Task<bool> PlayAdAsync(Media media);

    /// <summary>Composed form: visual, own audio, or both; a silent still lets the bed play.</summary>
    Task<bool> PlayAdAsync(AdPlayback ad);
    Task PlayAsync();
    Task PauseAsync();
    Task StopAsync();

    /// <summary>Moves the playhead, clamped to the song; an end click is a position, not error.</summary>
    Task SeekAsync(TimeSpan position);

    /// <summary>Clamped to Min/MaxPitch: set, not heard; the transcode rebuilds after a delay.</summary>
    Task SetPitchAsync(int semitones);

    /// <summary>Clamped to Min/MaxTempo; shares one settle with <see cref="SetPitchAsync"/>.</summary>
    Task SetTempoAsync(int tempo);

    /// <summary>Separately-mixable voices in the file; empty for an ordinary single-track song.</summary>
    IReadOnlyList<AudioTrack> AudioTracks { get; }

    /// <summary>Percent of the original lead vocal riding on the music. Zero by default.</summary>
    int LeadVolume { get; }

    /// <summary>Backing voice percent; starts at the machine setting, which defaults to full.</summary>
    int BackingVolume { get; }

    /// <summary>Clamped to 0..100; shares the pitch/tempo settle, so a balance costs one break.</summary>
    Task SetLeadVolumeAsync(int volume);

    /// <inheritdoc cref="SetLeadVolumeAsync"/>
    Task SetBackingVolumeAsync(int volume);
}

// Stopping is appended so the existing numeric values stay stable for telemetry.
public enum PlaybackState { Stopped, Playing, Paused, Stopping }
