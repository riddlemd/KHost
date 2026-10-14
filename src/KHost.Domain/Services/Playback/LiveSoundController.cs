using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common.Media;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.Playback;

/// <summary>How the loaded song sounds — key, tempo and the level of each part — changed live,
/// recorded on the performance, and carried to the room.</summary>
/// <remarks>Owned by <see cref="PlaybackService"/>. A change reaches the room as a stem level on a
/// display that mixes, else as a rebuild of the stream at the playhead, collapsed over a burst of
/// presses; the rebuild itself is the owner's.</remarks>
internal sealed class LiveSoundController
{
    private readonly IPerformanceService _performanceService;
    private readonly IMessageBroker _broker;
    private readonly DisplayLink _link;
    private readonly ILogger _logger;
    private readonly Func<TimeSpan> _settleDelay;
    private readonly Func<Performance?> _performance;
    private readonly Func<MediaRendition?> _rendition;
    private readonly Func<Task> _rebuild;

    // Interlocked only: the pending rebuild, replaced by each change inside the settle.
    private CancellationTokenSource? _rebuildSettle;

    /// <param name="settleDelay">Read per use: the App Settings page writes it live.</param>
    /// <param name="performance">The row a change is recorded on; null for an ad.</param>
    /// <param name="rendition">What the displays are playing, if anything.</param>
    /// <param name="rebuild">Reopens the stream at the playhead with the current sound.</param>
    public LiveSoundController(
        IPerformanceService performanceService,
        IMessageBroker broker,
        DisplayLink link,
        ILogger logger,
        Func<TimeSpan> settleDelay,
        Func<Performance?> performance,
        Func<MediaRendition?> rendition,
        Func<Task> rebuild)
    {
        _performanceService = performanceService;
        _broker = broker;
        _link = link;
        _logger = logger;
        _settleDelay = settleDelay;
        _performance = performance;
        _rendition = rendition;
        _rebuild = rebuild;
    }

    // Every property below is unguarded, like the transport state it rides with.
    public int Pitch { get; private set; }
    public int Tempo { get; private set; }

    /// <summary>The named voices in the loaded file, empty for the ordinary single-track song.</summary>
    public IReadOnlyList<AudioTrack> AudioTracks { get; private set; } = [];

    public int LeadVolume { get; private set; }
    public int BackingVolume { get; private set; } = AudioMix.DefaultBackingVolume;

    // Replaced whole on every change, never edited: a mix handed to a renderer holds the old one.
    public IReadOnlyDictionary<string, int> VoiceVolumes { get; private set; } = new Dictionary<string, int>();

    /// <summary>What the open stream is mixed to, or null when the file has nothing to balance.</summary>
    public AudioMix? CurrentMix =>
        AudioTracks.Count == 0 ? null : new AudioMix(AudioTracks, LeadVolume, BackingVolume) { VoiceVolumes = VoiceVolumes };

    /// <summary>Takes key, tempo and the two levels a performance was sung at.</summary>
    /// <param name="defaultBackingVolume">For a performance nobody has mixed: the machine setting
    /// answers, and goes on answering if that setting later changes.</param>
    public void TakeFrom(Performance performance, int defaultBackingVolume)
    {
        Pitch = performance.Pitch;
        Tempo = performance.Tempo;
        LeadVolume = AudioLevels.ClampVolume(performance.LeadVolume);
        BackingVolume = AudioLevels.ClampVolume(performance.BackingVolume ?? defaultBackingVolume);
    }

    /// <summary>Takes the loaded file's voices, each at the level the performance gave it.</summary>
    public void TakeTracks(Performance performance, IReadOnlyList<AudioTrack> tracks)
    {
        AudioTracks = tracks;

        // An entry for every voice, so the unnamed lead's level never reaches a named singer's stem.
        VoiceVolumes = VoicesIn(AudioTracks).ToDictionary(
            voice => voice,
            voice => AudioLevels.ClampVolume(
                performance.VoiceVolumes?.GetValueOrDefault(voice, AudioMix.DefaultLeadVolume) ?? AudioMix.DefaultLeadVolume));
    }

    /// <summary>Cleared, not carried: the next thing loaded brings its own, and an ad has neither.</summary>
    public void Clear(int defaultBackingVolume)
    {
        Pitch = 0;
        Tempo = 0;

        AudioTracks = [];
        LeadVolume = AudioMix.DefaultLeadVolume;
        BackingVolume = defaultBackingVolume;
        VoiceVolumes = new Dictionary<string, int>();
    }

    /// <summary>Cancelled rather than left to fire: it would otherwise reopen an encode for the
    /// song that has just been torn down.</summary>
    public void CancelPendingRebuild() => Interlocked.Exchange(ref _rebuildSettle, null)?.Cancel();

    public async Task SetPitchAsync(int semitones)
    {
        var target = Math.Clamp(semitones, IPlaybackService.MinPitch, IPlaybackService.MaxPitch);

        if (target == Pitch) return;

        Pitch = target;
        _logger.LogInformation("Pitch set to {Semitones:+#;-#;0}", target);

        await AfterSoundChangeAsync();
    }

    public async Task SetTempoAsync(int tempo)
    {
        var target = Math.Clamp(tempo, IPlaybackService.MinTempo, IPlaybackService.MaxTempo);

        if (target == Tempo) return;

        Tempo = target;
        _logger.LogInformation("Tempo set to {Tempo:+#;-#;0}%", target);

        await AfterSoundChangeAsync();
    }

    public async Task SetLeadVolumeAsync(int volume)
    {
        var target = AudioLevels.ClampVolume(volume);

        if (target == LeadVolume) return;

        LeadVolume = target;
        _logger.LogInformation("Lead vocal set to {Volume}%", target);

        await AfterMixChangeAsync(AudioTrackRole.Lead, target);
    }

    public async Task SetBackingVolumeAsync(int volume)
    {
        var target = AudioLevels.ClampVolume(volume);

        if (target == BackingVolume) return;

        BackingVolume = target;
        _logger.LogInformation("Backing vocals set to {Volume}%", target);

        await AfterMixChangeAsync(AudioTrackRole.Backing, target);
    }

    public async Task SetVoiceVolumeAsync(string voice, int volume)
    {
        var target = AudioLevels.ClampVolume(volume);

        if (!VoiceVolumes.TryGetValue(voice, out var current))
        {
            _logger.LogWarning("No lead in this song is sung by {Voice}; level left alone", voice);
            return;
        }

        if (target == current) return;

        VoiceVolumes = new Dictionary<string, int>(VoiceVolumes) { [voice] = target };
        _logger.LogInformation("Lead vocal for {Voice} set to {Volume}%", voice, target);

        await AfterMixChangeAsync(AudioTrackRole.Lead, target, voice);
    }

    /// <summary>Rebuilds the stream once the settle runs out, a later call restarting it.</summary>
    public void ScheduleRebuild()
    {
        var settle = new CancellationTokenSource();
        var superseded = Interlocked.Exchange(ref _rebuildSettle, settle);
        superseded?.Cancel();
        superseded?.Dispose();

        var token = settle.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_settleDelay(), token);
                await _rebuild();
            }
            catch (OperationCanceledException)
            {
                // Another press landed inside the delay, or the song ended.
            }
            finally
            {
                Interlocked.CompareExchange(ref _rebuildSettle, null, settle);
                settle.Dispose();
            }
        }, CancellationToken.None);
    }

    private static IEnumerable<string> VoicesIn(IEnumerable<AudioTrack> tracks) => tracks
        .Where(t => t.Role == AudioTrackRole.Lead && t.Voice is not null)
        .Select(t => t.Voice!)
        .Distinct(StringComparer.Ordinal);

    /// <summary>A moved voice, which a mixing display takes as a gain rather than a new encode.</summary>
    /// <remarks>Falls through to the rebuild whenever the displays cannot do it themselves, so this
    /// is a shortcut past <see cref="AfterSoundChangeAsync"/> and never a second way of doing it.</remarks>
    private async Task AfterMixChangeAsync(AudioTrackRole role, int volume, string? voice = null)
    {
        if (await TryMoveStemAsync(role, volume, voice))
        {
            // The rebuild path announces and persists on its way through; this one still must.
            _broker.Announce(new PlaybackChanged());
            await PersistSoundAsync();
            return;
        }

        await AfterSoundChangeAsync();
    }

    /// <summary>Shared by key, speed and any level a display cannot move itself, so changing
    /// several costs the song one break rather than one each.</summary>
    private async Task AfterSoundChangeAsync()
    {
        // Before the encode is touched, so the readout answers the button rather than ffmpeg.
        _broker.Announce(new PlaybackChanged());

        // Not on the settle below: a song ending inside that window would lose the change.
        await PersistSoundAsync();

        // Nothing open to rebuild; the row is written and the next load reads it back.
        if (_rendition() is null) return;

        ScheduleRebuild();
    }

    /// <summary>Writes key, speed and levels onto the row the history is read from. An ad has none.</summary>
    private async Task PersistSoundAsync()
    {
        if (_performance() is not { } performance) return;

        performance.Pitch = Pitch;
        performance.Tempo = Tempo;
        performance.LeadVolume = LeadVolume;
        performance.BackingVolume = BackingVolume;

        // Merged, not replaced: a song whose probe came back empty must not wipe how it was sung.
        if (VoiceVolumes.Count > 0)
        {
            var voices = performance.VoiceVolumes is null ? [] : new Dictionary<string, int>(performance.VoiceVolumes);
            foreach (var (voice, level) in VoiceVolumes) voices[voice] = level;
            performance.VoiceVolumes = voices;
        }

        try
        {
            await _performanceService.UpdateAsync(performance);
        }
        catch (Exception ex)
        {
            // Never fatal: the room already has the new key, and losing the record must not
            // take the song off the screens.
            _logger.LogWarning(ex, "Could not record the rate on performance {PerformanceId}", performance.Id);
        }
    }

    /// <summary>Moves a voice on the displays instead of rebuilding the stream, where that works.</summary>
    /// <returns>False when the display is hearing the host's own mix, which only a new encode can
    /// change.</returns>
    private async Task<bool> TryMoveStemAsync(AudioTrackRole role, int volume, string? voice)
    {
        if (_rendition() is not { Stems.Count: > 0 }) return false;

        return await _link.TryMoveStemAsync(role, volume, voice);
    }
}
