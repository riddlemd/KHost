using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Gap after a performance; handlers can't await, so filling work registers here instead.</summary>
public sealed class PerformanceEndedEventArgs : EventArgs
{
    private readonly List<Task> _fills = [];

    /// <summary>Claims the gap for <paramref name="work"/>; break music does not return until it
    /// completes.</summary>
    /// <param name="work">Already started; call from inside the handler, before it returns.</param>
    public void Fill(Task work) => _fills.Add(work);

    /// <summary>Completes once every registered fill has, at once when there is none.</summary>
    /// <remarks>The host awaits it after raising the event; a handler has no reason to.</remarks>
    public Task WhenFilledAsync() => _fills.Count == 0 ? Task.CompletedTask : Task.WhenAll(_fills);
}

/// <summary>What is on the main channel — a singer's song or an ad — and the transport over it.</summary>
/// <remarks>Host-owned; a plugin has nothing to implement. A plugin may take it to read what is
/// playing or to drive transport. One thing plays at a time, on the one connected display; the
/// displays' own reports set <see cref="Position"/>. A host singleton, callable from any thread.
/// Every state change announces <see cref="KHost.Abstractions.Messaging.Messages.PlaybackChanged"/>;
/// <see cref="PositionChanged"/> and <see cref="PerformanceEnded"/> stay plain events, for the
/// reasons given on each.</remarks>
public interface IPlaybackService : IDisposable
{
    /// <summary>Semitones; past this, the shift's artefacts cost more than the transposition is worth.</summary>
    const int MaxPitch = 6;

    /// <summary>The lowest key shift, in semitones; the mirror of <see cref="MaxPitch"/>.</summary>
    const int MinPitch = -6;

    /// <summary>Percent either side of recorded speed; past this the stretch smears too badly.</summary>
    const int MaxTempo = 50;

    /// <summary>The slowest tempo, in percent; the mirror of <see cref="MaxTempo"/>.</summary>
    const int MinTempo = -50;


    /// <summary>A performance finished, raised before break music returns; never raised for an ad.</summary>
    /// <remarks>Raised whether the song ran out or a host stopped it, after the singer has been
    /// dequeued and the queue rotated. A handler cannot be awaited, so work that should fill the gap
    /// — an ad, say — registers through <see cref="PerformanceEndedEventArgs.Fill"/>; playback waits
    /// for all of it before bringing break music back.</remarks>
    event EventHandler<PerformanceEndedEventArgs>? PerformanceEnded;

    /// <summary>Twice a second while playing; only <see cref="Position"/> moves, so redraw only.</summary>
    /// <remarks>Raised off a timer thread. Deliberately not a broker message: it fires all night, and
    /// nothing else changes with it.</remarks>
    event EventHandler? PositionChanged;

    /// <summary>The singer's turn loaded now, or null when idle or playing an ad.</summary>
    Performance? CurrentPerformance { get; }

    /// <summary>The media loaded now, song or ad, or null when idle. An audio-only ad leaves it
    /// null.</summary>
    Media? CurrentMedia { get; }

    /// <summary>Who's singing; the venue decides if a differing recorded name shows. Null if idle.</summary>
    /// <remarks>Settled when the song loads, so a venue edit mid-song never renames the singer.</remarks>
    string? CurrentSingerName { get; }

    /// <summary>Whether the main channel is carrying an ad rather than a singer's song.</summary>
    bool IsPlayingAd { get; }

    /// <summary>What the main channel is carrying now — nothing, a song or video ad, or an ad's
    /// still — as facts a display pictures for itself.</summary>
    /// <remarks>Moves only with <see cref="KHost.Abstractions.Messaging.Messages.PlaybackChanged"/>,
    /// which is its announcement; that message also fires when the program has not moved, so
    /// compare by value. Set before the display is handed the load, so a provider reading it during
    /// <see cref="IDisplayProvider.LoadAsync"/>
    /// sees the program being loaded.</remarks>
    PlaybackProgram CurrentProgram { get; }

    /// <summary>Where the transport stands.</summary>
    PlaybackState State { get; }

    /// <summary>The playhead, in song time; zero when nothing is loaded.</summary>
    TimeSpan Position { get; }

    /// <summary>The singer whose song has started playing, or null before play and once it
    /// ends.</summary>
    Guid? CurrentlyPerformingUserId { get; }

    /// <summary>How long the current stop is fading out for; null when not stopping.</summary>
    TimeSpan? StopFadeDuration { get; }

    /// <summary>Semitones from the performance on load; applied by the host before the stream
    /// reaches a display.</summary>
    int Pitch { get; }

    /// <summary>Percent either side of recorded speed; retimes the picture with the audio.</summary>
    int Tempo { get; }

    /// <summary>Whether any display is connected; with none, playback produces no output.</summary>
    Task<bool> HasConnectedScreenAsync();

    /// <summary>Loads a singer's song onto the display, ready for <see cref="PlayAsync"/>.</summary>
    /// <remarks>Refused, silently, for media that is not Ready; refused for media its gate will not
    /// play at <see cref="MediaAction.Play"/>, flashing the gate's reason when it gives one. Otherwise
    /// takes over from any ad, suspends break music, restores the performance's key, tempo and mix,
    /// moves the singer to the top of the queue and holds them there until the song ends.</remarks>
    /// <exception cref="KHost.Abstractions.Exceptions.KHostException">When the song cannot be
    /// prepared for the display. Playback is left idle and break music restored, so the console
    /// stays usable.</exception>
    Task LoadAsync(Performance performance, Media media);

    /// <summary>Plays media on nobody's turn; ends without dequeuing. False if refused or nowhere.</summary>
    Task<bool> PlayAdAsync(Media media);

    /// <summary>Composed form: visual, own audio, or both; a silent still lets the bed play.</summary>
    /// <returns>False when the ad is empty or has no duration, a part is not Ready, a performance is
    /// loaded, no display is connected, or the display would not play it. An ad never plays over a
    /// singer.</returns>
    /// <remarks>Runs for the ad's duration and then ends itself, restoring break music.</remarks>
    Task<bool> PlayAdAsync(AdPlayback ad);

    /// <summary>Starts or resumes what is loaded.</summary>
    /// <remarks>Does nothing when nothing is loaded or it is already playing, and refuses, silently,
    /// when no display is connected: without one the clock would run the turn out unheard. Pressed
    /// during a stop's fade, it cancels the stop.</remarks>
    Task PlayAsync();

    /// <summary>Holds the song where it is. Does nothing unless playing.</summary>
    Task PauseAsync();

    /// <summary>Ends what is playing, fading out first when it can.</summary>
    /// <remarks>Fades only while playing and only on a display that can fade, and waits the fade out
    /// before finishing, so the call can take seconds. A singer's song then ends as though it had run
    /// out: dequeued, queue rotated, <see cref="PerformanceEnded"/> raised. A play or load during the
    /// fade supersedes the stop. A second call while stopping does nothing.</remarks>
    Task StopAsync();

    /// <summary>Moves the playhead, clamped to the song; an end click is a position, not error.</summary>
    /// <remarks>Works before play as well as during it; does nothing with nothing loaded.</remarks>
    Task SeekAsync(TimeSpan position);

    /// <summary>Clamped to Min/MaxPitch: set, not heard, until the stream catches up.</summary>
    /// <remarks>The display hears the change after a short settle, so several presses in quick
    /// succession cost the room one break rather than one each. Saved on the performance at once, so
    /// the next load of it keeps the key.</remarks>
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
    /// <remarks>On a display that mixes the voices itself the change lands at once, with no break.
    /// Saved on the performance.</remarks>
    Task SetLeadVolumeAsync(int volume);

    /// <inheritdoc cref="SetLeadVolumeAsync"/>
    Task SetBackingVolumeAsync(int volume);
}

// Stopping is appended so the existing numeric values stay stable for telemetry.
/// <summary>Where the main channel stands.</summary>
public enum PlaybackState
{
    /// <summary>Nothing is loaded on the main channel.</summary>
    Stopped,

    /// <summary>A song or ad is playing and the playhead is moving.</summary>
    Playing,

    /// <summary>Loaded and held at the playhead.</summary>
    Paused,

    /// <summary>A stop was asked for and the song is fading out; the current performance and media
    /// stay set until it is done. A play or load now supersedes the stop.</summary>
    Stopping
}
