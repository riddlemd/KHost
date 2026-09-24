using KHost.Abstractions.Models;
using KHost.Abstractions.Services.IPC;
using KHost.Abstractions.Services;
using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KHost.Common.Media;
using KHost.Domain.Services.Screens;

namespace KHost.Domain.Services;

public class PlaybackService : BaseService, IPlaybackService, IStartsWithTheHost
{
    public sealed class ServiceOptions
    {
        public const string SectionName = "Playback";

        /// <summary>How long screens fade audio and video out for on stop. Zero stops instantly.</summary>
        public TimeSpan StopFadeDuration { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>Collapses a burst of presses into one restart, each a hole in the song.</summary>
        public TimeSpan PitchSettleDelay { get; set; } = TimeSpan.FromMilliseconds(600);

        /// <summary>Where backing sits on an unmixed song; the lead has none, a singer replaces it.</summary>
        public int DefaultBackingVolume { get; set; } = AudioMix.DefaultBackingVolume;

        /// <summary>How long a replaced transcode stays before deletion.</summary>
        /// <remarks>No second player exists mid-fetch, and a receiver reads a 404 body as media.</remarks>
        public TimeSpan StreamRetireGrace { get; set; } = TimeSpan.FromSeconds(8);
    }

    private const int ClockIntervalMs = 500;

    // _timer's swap is read-then-assign, reached from the UI dispatcher and Task.Run continuations at once;
    // unguarded, the loser's Timer becomes unreachable but keeps ticking, driving a second clock forever.
    private readonly Lock _clockLock = new();
    private readonly IMessageBroker _broker;
    private Timer? _timer;

    // A tick that outruns the interval must not overlap the next: two in flight both see the song
    // ended and rotate the singer away twice.
    private int _ticking;

    private DateTime _lastTick;

    // Connect and disconnect both arrive as fire-and-forget continuations, so without this they
    // can interleave and a reconnect's resume races the disconnect's pause.
    private readonly SemaphoreSlim _screenSyncLock = new(1, 1);

    // The receiver app session the current song was loaded into. Compared rather than the device
    // id: a receiver that restarts is the same device having forgotten everything.
    private Guid? _displaySessionId;

    private IDisposable? _displaySubscription;
    private IDisposable? _venueSubscription;

    private IAnalyticsActivity? _sessionActivity;

    // What the displays are playing for the current song, if anything. It carries the host-side
    // transcode when the renderer started one, and nothing when the displays play the parts direct.
    private MediaRendition? _rendition;

    private CancellationTokenSource? _reopenSettle;

    // The ad on the main channel. Its Duration, not any one file's, is what the clock runs out.
    private AdPlayback? _ad;

    // An ad's own audio track, which borrows the background channel from break music.
    private MediaStreamSession? _adAudioStream;

    private readonly ISingerQueueService _singerQueueService;
    private readonly IPerformanceService _performanceService;
    private readonly IVenuesService _venuesService;
    private readonly IAnalyticsService _analytics;
    private readonly IScreenServer _screenServer;
    private readonly IMediaStreamService _mediaStreams;
    private readonly IMediaRendererService _renderers;
    private readonly IReadOnlyList<IDisplayProvider> _displays;
    private readonly IBreakMusicService _breakMusic;
    private readonly IMediaService _mediaService;
    private readonly IOptionsMonitor<ServiceOptions> _optionsMonitor;
    private readonly IAudioTrackService _audioTracks;
    private readonly IMediaGateService _mediaGate;
    private readonly ITimedLyricsService _timedLyrics;

    /// <summary>The words sent for the song now loaded, kept so a screen joining mid-song gets
    /// them too. Null for a song that has none, which is most of them.</summary>
    private TimedLyrics? _currentLyrics;
    private readonly IFlashService _flash;

    // Read per use rather than captured: the App Settings page writes the overlay live, and a
    // value snapshotted at startup would leave the console needing a restart to honour it.
    private ServiceOptions Options => _optionsMonitor.CurrentValue;

    public event EventHandler? PositionChanged;
    public event EventHandler<PerformanceEndedEventArgs>? PerformanceEnded;

    public Performance? CurrentPerformance { get; private set; }
    public Media? CurrentMedia { get; private set; }
    public string? CurrentSingerName { get; private set; }

    /// <summary>Whether the main channel is carrying an ad rather than a singer's song.</summary>
    public bool IsPlayingAd => _ad is not null;
    public PlaybackState State { get; private set; } = PlaybackState.Stopped;
    public TimeSpan Position { get; private set; }
    public Guid? CurrentlyPerformingUserId { get; private set; }
    public TimeSpan? StopFadeDuration { get; private set; }
    public int Pitch { get; private set; }
    public int Tempo { get; private set; }

    /// <summary>The named voices in the loaded file, empty for the ordinary single-track song.</summary>
    public IReadOnlyList<AudioTrack> AudioTracks { get; private set; } = [];

    public int LeadVolume { get; private set; }
    public int BackingVolume { get; private set; } = AudioMix.DefaultBackingVolume;

    /// <summary>Song seconds per wall-clock second, from the open stream not the wanted tempo.</summary>
    /// <remarks>The room is still on the old rate until the stream reopens.</remarks>
    private double Rate => _rendition is { } rendition ? StreamRate.FromTempo(rendition.Tempo) : 1.0;

    public PlaybackService(
        ILogger<PlaybackService> logger,
        ISingerQueueService singerQueueService,
        IPerformanceService performanceService,
        IVenuesService venuesService,
        IAnalyticsService analytics,
        IScreenServer screenServer,
        IMediaStreamService mediaStreams,
        IMediaRendererService renderers,
        IEnumerable<IDisplayProvider> displayProviders,
        IBreakMusicService breakMusic,
        IMediaService mediaService,
        IOptionsMonitor<ServiceOptions> options,
        IAudioTrackService audioTracks,
        IMediaGateService mediaGate,
        ITimedLyricsService timedLyrics,
        IFlashService flash,
        IMessageBroker broker)
        : base(logger)
    {
        _broker = broker;
        _singerQueueService = singerQueueService;
        _performanceService = performanceService;
        _venuesService = venuesService;
        _analytics = analytics;
        _screenServer = screenServer;
        _mediaStreams = mediaStreams;
        _renderers = renderers;
        // Every display the host can reach, screens included — the screens are a provider too.
        // One is connected at a time, which the providers enforce; this is simply all of them.
        _displays = [.. displayProviders];
        _breakMusic = breakMusic;
        _mediaService = mediaService;
        _optionsMonitor = options;
        _audioTracks = audioTracks;
        _mediaGate = mediaGate;
        _timedLyrics = timedLyrics;
        _flash = flash;

        _screenServer.ScreenConnected += OnScreenConnected;
        _screenServer.ScreenDisconnected += OnScreenDisconnected;
        _screenServer.StateReceived += OnScreenStateReceived;
        foreach (var display in _displays)
            display.PlaybackStatusChanged += OnDisplayStatusReceived;
        _displaySubscription = _broker.Subscribe<DisplaysChanged>(message => { _ = Task.Run(SyncDisplaySessionAsync); });

        // The card is the venue's, so a venue edit is news about what should be on screen. Without this it
        // changed only at the next transition, a host looking at the old one until a singer came and went.
        _venueSubscription = _broker.Subscribe<SelectedVenueChanged>(
            message => { _ = Task.Run(RefreshIdleCardAsync); });
    }

    /// <summary>The name to show, resolved once at load: a venue edit mid-song never renames it.</summary>
    private async Task<string?> NameForAsync(Performance performance)
    {
        var singer = _singerQueueService.Users.FirstOrDefault(user => user.Id == performance.SingerId)?.Name?.Trim();
        var recorded = performance.SungAs?.Trim();

        if (string.IsNullOrEmpty(recorded))
            return string.IsNullOrEmpty(singer) ? null : singer;

        if (string.IsNullOrEmpty(singer))
            return recorded;

        var venue = await _venuesService.ReadSelectedVenueAsync();

        return venue?.Settings.AllowAliases == true ? recorded : singer;
    }

    /// <summary>Re-reads the venues card, but only while nothing is playing.</summary>
    /// <remarks>A still over a singer is worse than a stale one.</remarks>
    private async Task RefreshIdleCardAsync()
    {
        if (CurrentPerformance is not null || IsPlayingAd)
            return;

        await ShowIdleCardAsync();
    }

    /// <summary>Whether the song has anywhere to come out — a screen, a television, anything.</summary>
    /// <remarks>One question now the screens are a display like the rest. Refusing to play with only
    /// a television attached would refuse the setup casting exists for.</remarks>
    public Task<bool> HasConnectedScreenAsync()
    {
        try
        {
            return Task.FromResult(ConnectedDisplay.Find(_displays) is not null);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to read the connected displays");
        }

        return Task.FromResult(false);
    }

    public async Task LoadAsync(Performance performance, Media media)
    {
        // Broken was already unplayable in spirit; a download still in flight is the same refusal
        // for a different reason, so the check is against Ready rather than either status by name.
        if (media.Status != MediaStatus.Ready)
        {
            Logger.LogWarning("Load refused: media {MediaId} is {Status}, not Ready", media.Id, media.Status);
            return;
        }

        // A plugin can gate its own rendered content, refusing it without a live account; it is
        // refused like a non-Ready row, and the gate's reason is flashed since nothing else would say why.
        var gate = await _mediaGate.EvaluateAsync(MediaAction.Play, media);
        if (!gate.Allowed)
        {
            Logger.LogInformation("Load refused: media {MediaId} is gated ({Reason})", media.Id, gate.Reason);
            if (!string.IsNullOrWhiteSpace(gate.Reason))
                _flash.Show(gate.Reason, FlashType.Warning);
            return;
        }

        ResetState();

        // A song takes the room back from an ad still running. The still comes down with the card
        // below, but an ad's own audio is on the background channel, which nothing else clears.
        if (IsPlayingAd)
            await CancelAdAsync();

        // The venue card is only for when nothing is playing.
        await ToDisplaysAsync(new HideImageCommand());

        // On load rather than on play: the host lines the next singer up while the room is still
        // listening to the bed, and a song that starts over the top of it is two songs at once.
        await _breakMusic.SuspendAsync();

        CurrentPerformance = performance;
        CurrentMedia = media;
        CurrentSingerName = await NameForAsync(performance);
        Position = TimeSpan.Zero;

        // After ResetState, which cleared them: a performance carries how it was sung.
        Pitch = performance.Pitch;
        Tempo = performance.Tempo;
        LeadVolume = AudioLevels.ClampVolume(performance.LeadVolume);

        // Null means nobody has mixed this one, so the machine setting answers, and goes on
        // answering if that setting later changes.
        BackingVolume = AudioLevels.ClampVolume(performance.BackingVolume ?? Options.DefaultBackingVolume);

        // Probed rather than stored: the answer costs one ffprobe, and a file replaced on disk
        // would otherwise keep whatever its tracks were called at import.
        //
        AudioTracks = await _audioTracks.ReadTracksAsync(media.FilePath);

        _sessionActivity = _analytics.StartActivity(AnalyticActivities.Session);
        _sessionActivity.SetTag("media_id", media.Id);

        await _singerQueueService.MoveUserToStartAsync(performance.SingerId);
        _singerQueueService.LockTopSlot();

        Logger.LogInformation("Loading media '{Title}' for performance {PerformanceId}", media.Title, performance.Id);

        try
        {
            await ToDisplaysAsync(await BuildLoadCommandAsync(media, TimeSpan.Zero));

            // After the load and before play: a screen holds the words until the next load, and one
            // that arrived mid-song would light every syllable already sung at once.
            await SendTimedLyricsAsync(media);
        }
        catch
        {
            // A load that dies here must still leave the console recoverable: remove stays disabled for
            // the current performance, play works for every row, and stop covers a state that never played.
            await AbandonLoadAsync();
            throw;
        }

        _broker.Announce(new PlaybackChanged());
    }

    /// <summary>Puts back what LoadAsync set up before it failed.</summary>
    /// <remarks>Deliberately not EndedAsync, since nothing was performed.</remarks>
    private async Task AbandonLoadAsync()
    {
        await CloseStreamAsync();

        CurrentPerformance = null;
        CurrentMedia = null;
        CurrentSingerName = null;

        _singerQueueService.UnlockTopSlot();

        ResetState();

        // Guarded so a bed that will not come back cannot replace the load failure the host is
        // about to be shown with one about break music.
        try
        {
            await _breakMusic.RestoreAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not restore break music after a failed load");
        }

        _broker.Announce(new PlaybackChanged());
    }

    /// <summary>The simple case: one file, its own length, whatever audio it carries.</summary>
    public Task<bool> PlayAdAsync(Media media) => PlayAdAsync(new AdPlayback
    {
        Visual = media,
        Duration = media.Duration ?? TimeSpan.Zero,
    });

    /// <summary>Plays media on the main channel that is nobodys turn.</summary>
    /// <remarks>Never dequeues or rotates: retiring a singer for an ad would cost their song.</remarks>
    public async Task<bool> PlayAdAsync(AdPlayback ad)
    {
        if (ad.IsEmpty)
        {
            Logger.LogWarning("Ad refused: nothing to show and nothing to play");
            return false;
        }

        // Nothing here is watching an ad the way a host watches a song, and the clock ends one by
        // its duration: without one this would hold the main channel all night.
        if (ad.Duration <= TimeSpan.Zero)
        {
            Logger.LogWarning("Ad refused: no duration");
            return false;
        }

        if (ad.Visual is { Status: not MediaStatus.Ready } visualStatus)
        {
            Logger.LogWarning("Ad refused: visual {MediaId} is {Status}, not Ready", visualStatus.Id, visualStatus.Status);
            return false;
        }

        if (ad.Audio is { Status: not MediaStatus.Ready } audioStatus)
        {
            Logger.LogWarning("Ad refused: audio {MediaId} is {Status}, not Ready", audioStatus.Id, audioStatus.Status);
            return false;
        }

        // Never over a singer. An ad waits for the gap rather than cutting a performance short.
        if (CurrentPerformance is not null)
        {
            Logger.LogWarning("Ad refused: a performance is loaded");
            return false;
        }

        ResetState();

        // Only what the room can hear takes the room. A silent still leaves the bed playing under
        // it rather than dropping the venue into fifteen seconds of nothing.
        if (ad.HasOwnAudio())
            await _breakMusic.SuspendAsync();

        CurrentMedia = ad.Visual;
        _ad = ad;
        Position = TimeSpan.Zero;

        if (!await HasConnectedScreenAsync())
        {
            Logger.LogWarning("Ad refused: no screens are connected");

            await EndedAsync();
            _broker.Announce(new PlaybackChanged());
            return false;
        }

        Logger.LogInformation("Playing ad '{Title}'", ad.Visual?.Title ?? ad.Audio?.Title);

        var playsOnMainChannel = ad.Visual is { } v && !MediaFormats.IsImage(v.Format);

        if (playsOnMainChannel)
        {
            await ToDisplaysAsync(new HideImageCommand());
            await ToDisplaysAsync(await BuildLoadCommandAsync(ad.Visual!, TimeSpan.Zero));
        }
        else if (ad.Visual is { } still)
        {
            await ToDisplaysAsync(new ShowImageCommand { Url = _mediaStreams.BuildImageUrl(still.Id), Scaling = still.ImageScaling });
        }

        // Opened at the offset rather than trimmed: a clip out of a longer file costs no re-encode,
        // and the host clock stops it at the ad's duration.
        if (ad.Audio is { } audio)
        {
            _adAudioStream = await _mediaStreams.OpenAsync(audio.FilePath, ad.AudioStart);

            await ToDisplaysAsync(new LoadBackgroundCommand
            {
                StreamUrl = _adAudioStream.PlaylistUrl,
                AutoPlay = true,
            });
        }

        _broker.Announce(new PlaybackChanged());

        if (!playsOnMainChannel)
        {
            // A still or an audio-only spot drives no media element on the main channel, so there
            // is nothing for the play path to start: the clock alone runs it out.
            State = PlaybackState.Playing;
            StartClock();

            _broker.Announce(new PlaybackChanged());
            return true;
        }

        await PlayAsync();

        if (State == PlaybackState.Playing)
            return true;

        // A refusal leaves no screen to run the clock down and clear this, so the ad would hold
        // the main channel and keep the bed suspended behind it.
        await EndedAsync();

        _broker.Announce(new PlaybackChanged());

        return false;
    }

    public async Task PlayAsync()
    {
        // Media rather than performance: an ad is loaded the same way and has no singer.
        if (CurrentMedia is null || State == PlaybackState.Playing) return;

        // Playing during a stop's fade supersedes it, but the screens have already been told to
        // fade out and drop the media, so they need it handed back before being told to play.
        var supersedesStop = State == PlaybackState.Stopping;

        // Without a screen there is no audio or video, but the position timer would still run
        // the performance to completion and rotate the singer away, burning their turn.
        if (!await HasConnectedScreenAsync())
        {
            Logger.LogWarning("Play refused: no screens are connected");
            return;
        }

        CurrentlyPerformingUserId = CurrentPerformance?.SingerId;

        // Leaving Stopping here is what tells an in-flight StopAsync to abandon its completion.
        State = PlaybackState.Playing;
        StopFadeDuration = null;
        StartClock();

        _analytics.RecordPlaybackStateTransition(PlaybackState.Playing);

        Logger.LogInformation("Playback started for user {UserId}", CurrentPerformance?.SingerId);

        if (supersedesStop && CurrentMedia is { } resumed)
        {
            Logger.LogInformation("Play superseded a stop; reloading the screens at {Position}", Position);

            await ReplayOntoDisplayAsync(resumed, seekPast: TimeSpan.Zero);
        }
        else
        {
            await ToDisplaysAsync(new PlayCommand());
        }

        _broker.Announce(new PlaybackChanged());
    }

    public async Task SeekAsync(TimeSpan position)
    {
        // Loaded is enough. A song sits in Stopped until Play, and scrubbing before starting it
        // (to skip a long intro) is exactly when a host reaches for the bar.
        if (CurrentMedia is not { } media)
            return;

        var target = Clamp(position, media.Duration);

        // A stream opened part-way through a song holds nothing before its own zero (a rate or mix change
        // rebuilds it at the playhead). Seeking back past that point clamps there, not to the true target.
        if (_rendition is { SeekableInPlace: false } open && target < open.StartOffset)
        {
            Logger.LogInformation("Seeking to {Position}, behind the stream; rebuilding it", target);

            await ReopenStreamAsync(target);
            return;
        }

        var wasPlaying = State == PlaybackState.Playing;

        // Stopped first: the tick would otherwise carry the old position over the new one.
        StopClock();
        Position = target;

        Logger.LogInformation("Seeking to {Position}", target);

        await ToDisplaysAsync(new SeekCommand { Position = target });

        if (wasPlaying)
            StartClock();

        _broker.Announce(new PlaybackChanged());
    }

    public async Task SetPitchAsync(int semitones)
    {
        var target = Math.Clamp(semitones, IPlaybackService.MinPitch, IPlaybackService.MaxPitch);

        if (target == Pitch) return;

        Pitch = target;
        Logger.LogInformation("Pitch set to {Semitones:+#;-#;0}", target);

        await AfterRateChangeAsync();
    }

    /// <summary>What the open stream is mixed to, or null when the file has nothing to balance.</summary>
    private AudioMix? CurrentMix =>
        AudioTracks.Count == 0 ? null : new AudioMix(AudioTracks, LeadVolume, BackingVolume);

    public async Task SetLeadVolumeAsync(int volume)
    {
        var target = AudioLevels.ClampVolume(volume);

        if (target == LeadVolume) return;

        LeadVolume = target;
        Logger.LogInformation("Lead vocal set to {Volume}%", target);

        await AfterMixChangeAsync(AudioTrackRole.Lead, target);
    }

    public async Task SetBackingVolumeAsync(int volume)
    {
        var target = AudioLevels.ClampVolume(volume);

        if (target == BackingVolume) return;

        BackingVolume = target;
        Logger.LogInformation("Backing vocals set to {Volume}%", target);

        await AfterMixChangeAsync(AudioTrackRole.Backing, target);
    }

    public async Task SetTempoAsync(int tempo)
    {
        var target = Math.Clamp(tempo, IPlaybackService.MinTempo, IPlaybackService.MaxTempo);

        if (target == Tempo) return;

        Tempo = target;
        Logger.LogInformation("Tempo set to {Tempo:+#;-#;0}%", target);

        await AfterRateChangeAsync();
    }

    /// <summary>A moved voice, which a mixing display takes as a gain rather than a new encode.</summary>
    /// <remarks>Falls through to the rebuild whenever the displays cannot do it themselves, so this
    /// is a shortcut past <see cref="AfterRateChangeAsync"/> and never a second way of doing it.</remarks>
    private async Task AfterMixChangeAsync(AudioTrackRole role, int volume)
    {
        if (await TryMoveStemAsync(role, volume))
        {
            // The rebuild path announces and persists on its way through; this one still must.
            _broker.Announce(new PlaybackChanged());
            await PersistRateAsync();
            return;
        }

        await AfterRateChangeAsync();
    }

    /// <summary>Shared by key and speed, so changing both costs the song one break rather than two.</summary>
    private async Task AfterRateChangeAsync()
    {
        // Before the transcode is touched, so the readout answers the button rather than ffmpeg.
        _broker.Announce(new PlaybackChanged());

        // Not on the settle below: a song ending inside that window would lose the change.
        await PersistRateAsync();

        // Nothing open to rebuild; the row is written and the next load reads it back.
        if (_rendition is null) return;

        var settle = new CancellationTokenSource();
        var superseded = Interlocked.Exchange(ref _reopenSettle, settle);
        superseded?.Cancel();
        superseded?.Dispose();

        var token = settle.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Options.PitchSettleDelay, token);
                await ReopenStreamAsync();
            }
            catch (OperationCanceledException)
            {
                // Another press landed inside the delay, or the song ended.
            }
            finally
            {
                Interlocked.CompareExchange(ref _reopenSettle, null, settle);
                settle.Dispose();
            }
        }, CancellationToken.None);
    }

    /// <summary>Writes key and speed onto the row the history is read from. An ad has neither.</summary>
    private async Task PersistRateAsync()
    {
        if (CurrentPerformance is not { } performance) return;

        performance.Pitch = Pitch;
        performance.Tempo = Tempo;
        performance.LeadVolume = LeadVolume;
        performance.BackingVolume = BackingVolume;

        try
        {
            await _performanceService.UpdateAsync(performance);
        }
        catch (Exception ex)
        {
            // Never fatal: the room already has the new key, and losing the record must not
            // take the song off the screens.
            Logger.LogWarning(ex, "Could not record the rate on performance {PerformanceId}", performance.Id);
        }
    }

    private static TimeSpan Clamp(TimeSpan position, TimeSpan? duration)
    {
        if (position < TimeSpan.Zero) return TimeSpan.Zero;

        return duration is { } end && position > end ? end : position;
    }

    public async Task PauseAsync()
    {
        if (State != PlaybackState.Playing)
            return;

        State = PlaybackState.Paused;
        StopClock();

        _analytics.RecordPlaybackStateTransition(PlaybackState.Paused);

        Logger.LogInformation("Playback paused at {Position}", Position);

        await ToDisplaysAsync(new PauseCommand());

        _broker.Announce(new PlaybackChanged());
    }

    public async Task StopAsync()
    {
        if (State == PlaybackState.Stopping)
            return;

        // Screens can only fade while frames are flowing, so a paused stop is instant on the
        // screen: showing "Fading out…" here would just stall the UI over a blanked screen.
        var fade = State == PlaybackState.Playing ? Options.StopFadeDuration : TimeSpan.Zero;

        // Same reasoning, asked of the device rather than the state: a receiver stops dead, and
        // the wait below would then be that many seconds of silence before the queue moves on.
        if (fade > TimeSpan.Zero && !DisplayCanFade())
            fade = TimeSpan.Zero;

        // Hold CurrentPerformance/CurrentMedia until the screens have finished fading, so the
        // UI can show the song winding down instead of blanking while it is still audible.
        StopClock();
        State = PlaybackState.Stopping;
        StopFadeDuration = fade > TimeSpan.Zero ? fade : null;

        Logger.LogInformation("Playback stopping (fade={Fade})", fade);

        _broker.Announce(new PlaybackChanged());

        await ToDisplaysAsync(new StopCommand { FadeDuration = fade });

        if (fade > TimeSpan.Zero)
            await Task.Delay(fade);

        // Play or Load during the fade supersedes this stop.
        if (State != PlaybackState.Stopping)
            return;

        _analytics.RecordPlaybackStateTransition(PlaybackState.Stopped);

        ResetState();

        Logger.LogInformation("Playback stopped");

        await EndedAsync();

        _broker.Announce(new PlaybackChanged());
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _screenServer.ScreenConnected -= OnScreenConnected;
            _screenServer.ScreenDisconnected -= OnScreenDisconnected;
            _screenServer.StateReceived -= OnScreenStateReceived;
            foreach (var display in _displays)
            display.PlaybackStatusChanged -= OnDisplayStatusReceived;
            _displaySubscription?.Dispose();
            _displaySubscription = null;
            _venueSubscription?.Dispose();
            _venueSubscription = null;

            // _screenSyncLock is deliberately not disposed: a detached sync may still be holding
            // it at shutdown, and its Release would then throw.
            StopClock();
        }
    }

    // Raised while ScreenServerService holds the same non-reentrant lock GetConnectedScreensAsync
    // waits on, so this work must leave the hub thread or it deadlocks.
    private void OnScreenConnected(object? sender, ScreenConnectionEventArgs e) =>
        _ = Task.Run(SyncNewScreenAsync);

    private void OnScreenDisconnected(object? sender, ScreenConnectionEventArgs e) =>
        _ = Task.Run(HandleScreenLossAsync);

    /// <summary>A receiver has no timeline of its own, so it sits idle until the next load.</summary>
    /// <remarks>Keyed on session rather than device, to catch a restarted receiver too.</remarks>
    private async Task SyncDisplaySessionAsync()
    {
        var joined = _displays.FirstOrDefault(display => display.SessionId is not null);

        if (joined?.SessionId is null)
        {
            _displaySessionId = null;
            return;
        }

        await _screenSyncLock.WaitAsync();
        try
        {
            // Read inside the lock: DisplaysChanged is announced for discovery as well, so several
            // land close together and only one of them may claim the session.
            if (joined.SessionId is not { } session || session == _displaySessionId) return;

            _displaySessionId = session;

            var media = CurrentMedia;

            // Hand over the transcode already running rather than opening a second one; if there is none,
            // whatever starts next loads itself: an image has no stream a device could take.
            if (_rendition is not { Url.Length: > 0 }
                || media is null
                || MediaFormats.IsImage(media.Format)) return;

            Logger.LogInformation(
                "Display session is new; loading '{Title}' at {Position}", media.Title, Position);

            await ReplayOntoDisplayAsync(media, seekPast: _rendition.StartOffset);
        }
        finally { _screenSyncLock.Release(); }
    }

    /// <summary>A screen joining mid-session has nothing loaded, so PlayCommand alone is rejected.</summary>
    private async Task SyncNewScreenAsync()
    {
        await _screenSyncLock.WaitAsync();
        try
        {
            var media = CurrentMedia;

            // A still has no stream to reload: reloading would try to transcode a picture, so it is
            // redrawn on the new screen. Keyed on format, not on being an ad: what matters is the picture.
            if (media is not null && MediaFormats.IsImage(media.Format))
            {
                await ToDisplaysAsync(new ShowImageCommand { Url = _mediaStreams.BuildImageUrl(media.Id), Scaling = media.ImageScaling });
                return;
            }

            // Nothing is playing, so the joiner gets the venue's card, not a bare placeholder. An
            // audio-only ad lands here too, since it holds no picture of its own.
            if (media is null)
            {
                await ShowIdleCardAsync();
                return;
            }

            // Mid-render (a load, or a song ending): whatever is rendering sends its own load, which
            // reaches this screen too. Rendering here as well opens a second transcode nothing closes.
            if (_rendition is null)
                return;

            // Reloading costs an ffmpeg spin-up; a running clock resumes the screen behind the UI.
            StopClock();

            Logger.LogInformation("Screen connected; reloading '{Title}' at {Position}", media.Title, Position);

            // Without the words a screen joining mid-song plays the audio and draws nothing: they
            // were sent once, when the song started, to whoever was connected then.
            await ReplayOntoDisplayAsync(media, seekPast: TimeSpan.Zero, withLyrics: true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to sync a newly connected screen");
        }
        finally
        {
            // Also covers the throwing path: a stopped clock under Playing freezes the UI.
            if (State == PlaybackState.Playing)
                StartClock(restart: false);

            _screenSyncLock.Release();
        }
    }

    private async Task HandleScreenLossAsync()
    {
        await _screenSyncLock.WaitAsync();
        try
        {
            if (State != PlaybackState.Playing)
                return;

            if (await HasConnectedScreenAsync())
                return;

            Logger.LogWarning("The display carrying the song went away mid-performance; parking it at the start");

            // Always back to zero, never picked up where it stopped. A receiver's idea of where it
            // was is its own — it buffers seconds ahead, reports a position it has not reached, and
            // comes back having forgotten the session — so resuming put the room somewhere nobody
            // asked for. Starting the turn again is the one outcome a host can predict, and it is
            // theirs to trigger: the song waits on the play button rather than lurching back to life.
            await PauseAsync();
            Position = TimeSpan.Zero;

            _broker.Announce(new PlaybackChanged());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to handle screen disconnect");
        }
        finally
        {
            _screenSyncLock.Release();
        }
    }

    /// <param name="restart">False leaves a running clock alone, checked and started as one step.</param>
    private void StartClock(bool restart = true)
    {
        lock (_clockLock)
        {
            if (!restart && _timer is not null) return;

            _lastTick = DateTime.UtcNow;
            _timer?.Dispose();
            _timer = new Timer(OnTick, null, ClockIntervalMs, ClockIntervalMs);
        }
    }

    private void StopClock()
    {
        lock (_clockLock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void ResetState()
    {
        StopClock();

        _sessionActivity?.Dispose();
        _sessionActivity = null;

        CurrentlyPerformingUserId = null;
        _currentLyrics = null;

        // Cancelled rather than left to fire: it would otherwise reopen a transcode for the song
        // that has just been torn down.
        Interlocked.Exchange(ref _reopenSettle, null)?.Cancel();

        State = PlaybackState.Stopped;
        StopFadeDuration = null;
        Position = TimeSpan.Zero;

        // Cleared, not carried: the next thing loaded brings its own, and an ad has neither.
        Pitch = 0;
        Tempo = 0;

        AudioTracks = [];
        LeadVolume = AudioMix.DefaultLeadVolume;
        BackingVolume = Options.DefaultBackingVolume;
    }

    /// <summary>Gives the screens the words for this song, or clears the last song's.</summary>
    /// <remarks>Never throws: a song whose lyrics could not be read still plays, the same as one
    /// that never had any. Sent even when there are none, or a screen keeps drawing the words of
    /// the song before this one over it.</remarks>
    private async Task SendTimedLyricsAsync(Media media)
    {
        TimedLyrics? lyrics = null;

        try { lyrics = await _timedLyrics.GetTimedLyricsAsync(media.FilePath); }
        catch (Exception ex) { Logger.LogWarning(ex, "Could not read the lyric timing for '{Title}'", media.Title); }

        _currentLyrics = lyrics;

        try { await ToDisplaysAsync(new SetTimedLyricsCommand { Lyrics = lyrics }); }
        catch (Exception ex) { Logger.LogWarning(ex, "Could not send the lyric timing for '{Title}'", media.Title); }
    }

    /// <summary>Throws when the transcode will not start: there is no playback without it.</summary>
    private async Task<LoadMediaCommand> BuildLoadCommandAsync(Media media, TimeSpan startOffset)
    {
        // Held, not closed: tearing down first would leave the room on buffered frames while the new one
        // spins up (or on nothing, if it fails). A failed rebuild costs the change, not the song.
        var superseded = _rendition;

        // Not swallowed: without a rendition there is nothing to send, and pretending otherwise
        // leaves the room staring at a screen that never starts.
        try
        {
            // The ad path reaches here and reads zero: PlayAdAsync resets the state first.
            _rendition = await _renderers.RenderAsync(new MediaRenderRequest
            {
                FilePath = media.FilePath,
                StartOffset = startOffset,
                Pitch = Pitch,
                Tempo = Tempo,
                Mix = CurrentMix,
                Target = DescribeTarget(),
            });
        }
        catch (Exception ex) when (ex is not KHostException)
        {
            throw new KHostException(
                $"KHost couldn't prepare \u201c{media.Title}\u201d for the screens, so nothing was sent to them.",
                "Check the file is still on the drive, then try again.",
                "KH-STREAM-OPEN",
                ex);
        }
        finally
        {
            // Retired rather than closed, and only if the replacement actually took: on failure
            // _rendition still points at the old one, and closing it would take the song too.
            if (!ReferenceEquals(superseded, _rendition))
                RetireSession(superseded?.Session);
        }

        return DescribeStream(media);
    }

    /// <summary>Rebuilds since ffmpeg fixes its filter graph at start, which costs a short silence.</summary>
    private async Task ReopenStreamAsync(TimeSpan? at = null)
    {
        await _screenSyncLock.WaitAsync();
        try
        {
            // A still holds no stream to rebuild, and an ad is nobody's song to transpose.
            if (CurrentMedia is not { } media || IsPlayingAd || _rendition is null)
                return;

            var position = at ?? Position;

            // Stopped first, as in SeekAsync: a tick mid-reload carries the old position past
            // the point the new stream is opening at.
            StopClock();

            // Moves the playhead as well when a seek asked for this, so the bar answers the click
            // rather than waiting for the first report off the new stream.
            Position = position;

            Logger.LogInformation(
                "Reopening '{Title}' at {Position} pitched {Semitones:+#;-#;0} tempo {Tempo:+#;-#;0}%",
                media.Title, position, Pitch, Tempo);

            // Opened at the playhead, not opened at zero and seeked: the stream's own zero moves
            // with it, which is what StreamStartOffset carries to the screens.
            await ToDisplaysAsync(await BuildLoadCommandAsync(media, position));

            // Re-read rather than captured before the rebuild: a host pause landing during it must
            // not be overridden by a Play the host never asked for.
            if (State != PlaybackState.Playing)
                return;

            // Set again because the old stream's reports moved the playhead during the rebuild.
            // Resumed where the new stream opens, never skipped ahead: the skip lands on segments
            // ffmpeg has not written, and the element starves. Covering the gap is the transport's
            // business — a screen keeps its old element playing until the new one has sound.
            Position = position;

            await ToDisplaysAsync(new PlayCommand());
        }
        catch (Exception ex)
        {
            // Never rethrown: this runs on the settle continuation, where nothing observes it.
            Logger.LogError(ex, "Failed to reopen the stream");
        }
        finally
        {
            // Also covers the throwing path: a stopped clock under Playing freezes the UI.
            if (State == PlaybackState.Playing)
                StartClock(restart: false);

            _screenSyncLock.Release();

            _broker.Announce(new PlaybackChanged());
        }
    }

    /// <summary>Hands the running stream to a display that has nothing loaded, and resumes it.</summary>
    /// <param name="seekPast">Where the stream itself opens; a playhead no further on needs no seek.</param>
    /// <param name="withLyrics">After the load and before the seek, the order LoadAsync uses.</param>
    private async Task ReplayOntoDisplayAsync(Media media, TimeSpan seekPast, bool withLyrics = false)
    {
        await ToDisplaysAsync(DescribeStream(media));

        if (withLyrics)
            await ToDisplaysAsync(new SetTimedLyricsCommand { Lyrics = _currentLyrics });

        var position = Position;

        if (position > seekPast)
            await ToDisplaysAsync(new SeekCommand { Position = position });

        // Re-read rather than captured before the awaits: a host pause or stop landing mid-replay
        // must win over a stale "was playing".
        if (State == PlaybackState.Playing)
            await ToDisplaysAsync(new PlayCommand());
    }

    private LoadMediaCommand DescribeStream(Media media) => new()
    {
        // Null when the display plays the parts directly and nothing was encoded for it. The two
        // are never both empty: a rendition with neither would be a song with nowhere to come from.
        StreamUrl = _rendition?.Url,
        StreamStartOffset = _rendition?.StartOffset ?? TimeSpan.Zero,
        Tempo = _rendition?.Tempo ?? 0,
        Stems = _rendition?.Stems ?? [],
    };

    /// <summary>What the display that is up can take, which decides what is worth producing.</summary>
    /// <remarks>Nothing connected asks for nothing special — whatever connects later triggers a
    /// reload.</remarks>
    private RenderTarget DescribeTarget()
        => ConnectedDisplay.Find(_displays) is { Device.SupportsStemMix: true }
            ? new RenderTarget { MixesStems = true }
            : RenderTarget.None;

    /// <summary>The same levels <c>BuildMixGraph</c> compiles into ffmpeg, so neither path drifts.</summary>
    private int LevelFor(AudioTrackRole role) => role switch
    {
        AudioTrackRole.Lead => LeadVolume,
        AudioTrackRole.Backing => BackingVolume,

        // The music is the reference the voices are set against, and carries no level of its own.
        _ => AudioMix.MaxVolume,
    };

    /// <summary>Moves a voice on the displays instead of rebuilding the stream, where that works.</summary>
    /// <returns>False when the display is hearing the host's own mix, which only a new encode can
    /// change.</returns>
    private async Task<bool> TryMoveStemAsync(AudioTrackRole role, int volume)
    {
        if (_rendition is not { Stems.Count: > 0 }) return false;

        // Unknown is not assumed capable here, unlike the fade: guessing wrong costs a mix change
        // that silently never lands, where guessing wrong on a fade costs a pause.
        if (ConnectedDisplay.Find(_displays) is not { Device.SupportsStemMix: true }) return false;

        await ToDisplaysAsync(new SetStemVolumeCommand { Role = role, Volume = volume });
        return true;
    }

    /// <summary>Ends an ad before its clock ran out.</summary>
    /// <remarks>None of what follows a finished one (the bed, the venue card) happens here.</remarks>
    private async Task CancelAdAsync()
    {
        Logger.LogInformation("Ad cut short");

        _ad = null;

        await ReleaseAdAudioAsync();
    }

    /// <summary>Gives the background channel back, so the bed or a song can take it.</summary>
    private async Task ReleaseAdAudioAsync()
    {
        if (_adAudioStream is null) return;

        await ToDisplaysAsync(new StopBackgroundCommand());

        var stream = _adAudioStream;
        _adAudioStream = null;

        await CloseSessionAsync(stream);
    }

    private async Task CloseStreamAsync()
    {
        var rendition = _rendition;
        _rendition = null;

        await CloseSessionAsync(rendition?.Session);
    }

    /// <summary>Lets a replaced session stand until whatever is still reading it has moved on.</summary>
    private void RetireSession(MediaStreamSession? stream)
    {
        if (stream is null) return;

        var grace = Options.StreamRetireGrace;

        if (grace <= TimeSpan.Zero)
        {
            _ = CloseSessionAsync(stream);
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(grace);
            await CloseSessionAsync(stream);
        }, CancellationToken.None);
    }

    /// <summary>Never throws: an orphaned ffmpeg is worth a warning, not the song.</summary>
    private async Task CloseSessionAsync(MediaStreamSession? stream)
    {
        if (stream is null) return;

        try { await _mediaStreams.CloseAsync(stream.Id); }
        catch (Exception ex) { Logger.LogWarning(ex, "Failed to close media stream {SessionId}", stream.Id); }
    }

    private async Task EndedAsync()
    {
        // Every path that finishes a performance runs through here, so it is the one place the
        // transcode has to stop. An orphaned ffmpeg would keep burning CPU for a song nobody plays.
        await CloseStreamAsync();

        var currentPerformance = CurrentPerformance;
        var wasAd = IsPlayingAd;

        CurrentPerformance = null;
        CurrentMedia = null;
        CurrentSingerName = null;
        _ad = null;

        if (wasAd)
        {
            Logger.LogInformation("Ad finished");

            // Handed back before the bed is restored, or break music would reclaim the channel
            // while the ad's own voiceover was still playing on it.
            await ReleaseAdAudioAsync();

            // No dequeue and no rotation: an ad is nobody's turn, and there is no slot to unlock
            // because none was ever locked for it.
            await _breakMusic.RestoreAsync();
            await ShowIdleCardAsync();
            return;
        }

        _singerQueueService.UnlockTopSlot();

        if (currentPerformance is null)
            return;

        Logger.LogInformation("Performance {PerformanceId} ended for user {UserId}", currentPerformance.Id, currentPerformance.SingerId);

        // Dequeue first so rotation's songs-sung-tonight count includes the finished song.
        await _performanceService.DequeueAsync(currentPerformance.SingerId, currentPerformance.Id);
        await _singerQueueService.RotateQueueAsync(currentPerformance.SingerId);

        // Raised before the bed comes back, and awaited, so an ad taking this gap is never
        // started underneath break music that was restored a moment earlier.
        var gap = new PerformanceEndedEventArgs();
        PerformanceEnded?.Invoke(this, gap);
        await gap.WhenFilledAsync();

        // The ad the gap started, not the one that ended: _ad was cleared above.
        if (_ad is { } next)
        {
            // A silent ad (a still with no voiceover) runs over the bed, not over nothing. Starting one
            // only suspends the bed when it has its own sound, so only the ended song put it down.
            if (!next.HasOwnAudio())
                await _breakMusic.RestoreAsync();

            return;
        }

        await _breakMusic.RestoreAsync();
        await ShowIdleCardAsync();
    }

    /// <summary>Puts the venues card up now that nothing is playing, hides it if unset.</summary>
    /// <remarks>An ads still never outlives the ad.</remarks>
    private async Task ShowIdleCardAsync()
    {
        try
        {
            var venue = await _venuesService.ReadSelectedVenueAsync();
            var brandingId = venue?.Settings.BrandingImageMediaId;

            if (brandingId is null)
            {
                await ToDisplaysAsync(new HideImageCommand());
                return;
            }

            var media = await _mediaService.ReadAsync(brandingId.Value);

            if (media is null || !MediaFormats.IsImage(media.Format))
            {
                await ToDisplaysAsync(new HideImageCommand());
                return;
            }

            // The venue's own scaling answer wins when set: the same picture can be the card in two rooms
            // of different shapes, and a host adjusting it here need not go edit the library image.
            await ToDisplaysAsync(new ShowImageCommand
            {
                Url = _mediaStreams.BuildImageUrl(media.Id),
                Scaling = venue!.Settings.BrandingImageScaling ?? media.ImageScaling,
            });
        }
        catch (Exception ex)
        {
            // Never fatal: the card is decoration, and failing to draw it must not stop the queue
            // moving on to the next singer.
            Logger.LogWarning(ex, "Could not show the venue card");
        }
    }

    /// <summary>Sends one command to whatever the song is coming out of.</summary>
    /// <remarks>One path for every display, screens included: the screens reach the host through a
    /// provider like anything else. A command nothing can show is not sent — the device says what
    /// it can draw, so a television is never handed a marquee it would drop on the floor.
    ///
    /// <para>A provider that throws is logged, never rethrown: the caller is moving the show on and
    /// a display that has gone must not stop it.</para></remarks>
    private async Task ToDisplaysAsync(IScreenCommand command)
    {
        if (ConnectedDisplay.Find(_displays) is not { } connected) return;

        // An unlisted device is under-reporting rather than refusing, so it is sent everything
        // and drops what it cannot draw through the interface's own defaults.
        if (connected.Device is { } device && !CanShow(device, command)) return;

        try { await DispatchAsync(connected.Provider, command); }
        catch (Exception ex)
        {
            Logger.LogWarning(
                ex, "Failed to send {Command} to {Provider}", command.GetType().Name, connected.Provider.Name);
        }
    }

    /// <summary>Whether the display carrying the song can ride it down rather than cut it.</summary>
    /// <remarks>A device that has not listed itself yet is taken to fade, the same permissive
    /// stance <see cref="ToDisplaysAsync"/> takes: over-waiting is a pause nobody hears, while
    /// under-waiting cuts a song off mid-word.</remarks>
    private bool DisplayCanFade()
        => ConnectedDisplay.Find(_displays) is { } connected
            && (connected.Device is null || connected.Device.SupportsFade);

    /// <summary>Whether this device can show this command at all.</summary>
    /// <remarks>Transport is never gated — every display plays the song. Only the drawn things ask,
    /// and each asks its own flag, because the host answers them differently: words it can composite
    /// into the picture, a marquee it simply leaves off.</remarks>
    private static bool CanShow(DisplayDevice device, IScreenCommand command) => command switch
    {
        SetTimedLyricsCommand => device.SupportsLyrics,
        SetStemVolumeCommand => device.SupportsStemMix,
        SetMarqueeCommand => device.SupportsMarquee,
        SetScreenQrCodesCommand => device.SupportsQrCodes,
        ShowNextSingerCommand or SetBreakMusicCardCommand or ShowImageCommand or HideImageCommand
            => device.SupportsImage,
        _ => true,
    };

    /// <summary>The one place a command becomes a call, so a provider sees named members.</summary>
    private static Task DispatchAsync(IDisplayProvider display, IScreenCommand command) => command switch
    {
        LoadMediaCommand c => display.LoadAsync(c),
        PlayCommand => display.PlayAsync(),
        PauseCommand => display.PauseAsync(),
        StopCommand c => display.StopAsync(c.FadeDuration),
        SeekCommand c => display.SeekAsync(c.Position),
        SetVolumeCommand c => display.SetVolumeAsync(c.Volume),
        SetStemVolumeCommand c => display.SetStemVolumeAsync(c),
        SetVideoCommand c => display.SetVideoAsync(c.Enabled),

        SetTimedLyricsCommand c => display.SetTimedLyricsAsync(c),
        SetMarqueeCommand c => display.SetMarqueeAsync(c),
        SetScreenQrCodesCommand c => display.SetQrCodesAsync(c),
        ShowNextSingerCommand c => display.ShowNextSingerAsync(c),
        SetBreakMusicCardCommand c => display.SetBreakMusicCardAsync(c),
        ShowImageCommand c => display.ShowImageAsync(c),
        HideImageCommand => display.HideImageAsync(),

        LoadBackgroundCommand c => display.LoadBackgroundAsync(c),
        PlayBackgroundCommand => display.PlayBackgroundAsync(),
        PauseBackgroundCommand => display.PauseBackgroundAsync(),
        StopBackgroundCommand c => display.StopBackgroundAsync(c.FadeDuration),
        SetBackgroundVolumeCommand c => display.SetBackgroundVolumeAsync(c.Volume),

        // No provider carries anything else.
        _ => Task.CompletedTask,
    };

    /// <summary>Follows the position the screen reached, not one the host asserts.</summary>
    /// <remarks>An HLS stream starts on a segment boundary, so an asserted clock is off from the first frame.</remarks>
    private void OnScreenStateReceived(object? sender, ScreenStateReceivedEventArgs e)
    {
        if (State != PlaybackState.Playing) return;
        if (e.State is not ScreenPlaybackState state) return;
        if (!state.IsPlaying || state.SampledAtUtc is not { } sampledAt) return;

        // The screen defines the song position, so the host's own clock follows it rather than
        // free-running: the timer is only an interpolator between these reports.
        var now = DateTime.UtcNow;

        Position = state.Position + ((now - sampledAt) * Rate);
        _lastTick = now;
    }

    /// <summary>A receiver buffers seconds, so a free-running timer would end a song too soon.</summary>
    /// <remarks>The singer would rotate away while the room still hears it.</remarks>
    private void OnDisplayStatusReceived(object? sender, DisplayPlaybackStatus status)
    {
        if (State != PlaybackState.Playing || !status.IsPlaying) return;

        // A screen is the better clock: its reports are timestamped against a measured offset,
        // where a receiver's are only timestamped on arrival.
        // Read off the provider's event-tracked connection, which never waits on the hub's lock.
        if (_displays.OfType<ScreenDisplayProvider>().Any(screens => screens.ConnectedDeviceId is not null)) return;

        Position = status.Position + ((DateTime.UtcNow - status.SampledAtUtc) * Rate);
        _lastTick = DateTime.UtcNow;
    }

    private async void OnTick(object? state)
    {
        // Dropped, not queued: a tick only interpolates the playhead, and the one already running
        // has the newer reading anyway.
        if (Interlocked.Exchange(ref _ticking, 1) == 1) return;

        try
        {
            await TickAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unhandled error in playback tick");
        }
        finally
        {
            Interlocked.Exchange(ref _ticking, 0);
        }
    }

    internal async Task TickAsync()
    {
        lock (_clockLock)
        {
            // Timer.Dispose does not wait out a callback already running, so a tick can land after
            // a pause or stop, and would otherwise run a parked song to its end.
            if (_timer is null) return;

            var now = DateTime.UtcNow;

            // Position is song time and the clock is wall time, so a retimed song covers more or
            // less of itself per tick.
            Position += (now - _lastTick) * Rate;
            _lastTick = now;
        }

        if (HasPlaybackEnded())
        {
            ResetState();

            Logger.LogInformation("Playback concluded");

            await EndedAsync();

            _broker.Announce(new PlaybackChanged());
            return;
        }

        // Not PlaybackChanged: the queue panels answer that one with a queue read and a media read
        // per singer, and at twice a second for a whole night that is the console's entire load.
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool HasPlaybackEnded()
    {
        // An ad runs for the length its playlist entry gave it, which is not any one file's: a
        // still has a default, and a voiceover may be a clip out of something far longer.
        var duration = _ad is { } ad ? ad.Duration : CurrentMedia?.Duration;

        return duration is { } d && Position >= d;
    }

    private static class AnalyticActivities
    {
        public const string Session = "playback.session";
    }
}
