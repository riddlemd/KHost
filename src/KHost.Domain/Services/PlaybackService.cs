using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KHost.Common.Media;
using KHost.Domain.Services.Playback;

namespace KHost.Domain.Services;

/// <summary>The transport: what is loaded, playing, paused or stopped, and what follows a turn.</summary>
/// <remarks>The playhead is <see cref="PlaybackClock"/>'s, key/tempo/mix is
/// <see cref="LiveSoundController"/>'s, an ad's extras are <see cref="AdChannels"/>', and the way
/// out to the display is <see cref="DisplayLink"/>'s; this decides when each is used.</remarks>
public class PlaybackService : BaseService, IPlaybackService, IStartsWithTheHost
{
    public sealed class ServiceOptions
    {
        public const string SectionName = "Playback";

        /// <summary>How long screens fade audio and video out for on stop. Zero stops instantly.</summary>
        public TimeSpan StopFadeDuration { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>Collapses a burst of key, tempo, mix or lyric-setting changes into one stream
        /// rebuild, each a hole in the song.</summary>
        /// <remarks>Bound from <c>Playback:PitchSettleDelay</c>, the key it was first saved under.</remarks>
        [ConfigurationKeyName("PitchSettleDelay")]
        public TimeSpan RebuildSettleDelay { get; set; } = TimeSpan.FromMilliseconds(600);

        /// <summary>Where backing sits on an unmixed song; the lead has none, a singer replaces it.</summary>
        public int DefaultBackingVolume { get; set; } = AudioMix.DefaultBackingVolume;

        /// <summary>The least run-up a song whose words the screen draws is given before its first
        /// words; zero gives none.</summary>
        /// <remarks>Tops a short intro up to this, never adds to one already this long. Read when a
        /// song's words are sent, so a change applies from the next song.</remarks>
        public int LeadInGraceSeconds { get; set; }

        /// <summary>Whether the host adds lead-ins the timed lyrics left out.</summary>
        /// <remarks>A change reaches the song already up: see <see cref="TimedLyricsSettingsChanged"/>.</remarks>
        public bool DynamicLeadIns { get; set; }

        /// <summary>The silence before a line that earns it a host-added lead-in.</summary>
        public int DynamicLeadInPauseSeconds { get; set; } = LeadInGenerator.DefaultLongPauseSeconds;

        /// <summary>Whether the host moves apart timed-lyric colours a colour-blind viewer would
        /// confuse.</summary>
        /// <remarks>A change reaches the song already up: see <see cref="TimedLyricsSettingsChanged"/>.</remarks>
        public bool ColorBlindFriendlyLyrics { get; set; }

        /// <summary>How far the words are moved against the music, in milliseconds; positive is later.</summary>
        /// <remarks>Clamped to <see cref="LyricsOffset.MaxMilliseconds"/> either way. A change reaches
        /// the song already up: see <see cref="TimedLyricsSettingsChanged"/>.</remarks>
        public int LyricsOffsetMilliseconds { get; set; }

        /// <summary>How long a replaced encode stays before deletion.</summary>
        /// <remarks>No second player exists mid-fetch, and a receiver reads a 404 body as media.</remarks>
        public TimeSpan StreamRetireGrace { get; set; } = TimeSpan.FromSeconds(8);

        /// <summary>The longest the playhead waits after a play for the display to say it is
        /// playing; zero never waits.</summary>
        /// <remarks>Bounded so a display that never reports still runs the song out.</remarks>
        public TimeSpan DisplayStartTimeout { get; set; } = TimeSpan.FromSeconds(15);
    }

    private readonly IMessageBroker _broker;

    // Serializes every path that builds a stream for the display or parks it: load, ad, session
    // sync, display loss and stream reopen. Connect and disconnect both arrive as fire-and-forget
    // continuations, so without it a reconnect's resume races the disconnect's pause.
    // Not re-entrant: nothing that holds it may reach LoadFromStartAsync or PlayAsync.
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    private readonly PlaybackClock _clock;
    private readonly DisplayLink _link;
    private readonly AdChannels _ads;
    private readonly LiveSoundController _sound;

    /// <summary>Where the display's end is concluded: off the hub thread that reported it.</summary>
    /// <remarks>A seam so a test can hold that work back until the clock has concluded, which is the
    /// one interleaving the single-fire claim exists for and the thread pool will not reproduce.</remarks>
    internal Action<Func<Task>> DeferConclusion { get; set; } = work => _ = Task.Run(work);

    private IDisposable? _displaySubscription;
    private IDisposable? _lyricSettingsSubscription;

    // Unguarded, like the transport state.
    private IAnalyticsActivity? _sessionActivity;

    // What the displays are playing for the current song, if anything. It carries the host-side
    // encode when the renderer started one, and nothing when the displays play the parts direct.
    // Built under _reloadLock; closed outside it. Swapped with Interlocked.Exchange only, so each
    // session is closed by whichever path took it out.
    private MediaRendition? _rendition;

    // Interlocked only. Moves on every reset, so a rebuild queued for the song before it can tell.
    private long _songGeneration;

    // Interlocked only. Which stop's fade is the live one; see StopAsync.
    private int _stopGeneration;

    // Unguarded, like the transport state.
    private PlaybackProgram _program = new PlaybackProgram.Idle();

    private readonly ISingerQueueService _singerQueueService;
    private readonly IPerformanceService _performanceService;
    private readonly IVenuesService _venuesService;
    private readonly IAnalyticsService _analytics;
    private readonly IMediaStreamService _mediaStreams;
    private readonly IMediaRendererService _renderers;
    private readonly IBreakMusicService _breakMusic;
    private readonly IOptionsMonitor<ServiceOptions> _optionsMonitor;
    private readonly IAudioTrackService _audioTracks;
    private readonly IMediaGateService _mediaGate;
    private readonly IFlashService _flash;
    private readonly ITimedLyricsService _timedLyrics;
    private readonly TimeProvider _time;

    // Read per use rather than captured: the App Settings page writes the overlay live, and a
    // value snapshotted at startup would leave the console needing a restart to honour it.
    private ServiceOptions Options => _optionsMonitor.CurrentValue;

    public event EventHandler? PositionChanged;
    public event EventHandler<PerformanceEndedEventArgs>? PerformanceEnded;

    // The transport state below is unguarded: written from host calls, display events and conclusion
    // continuations alike, relying on their rarely overlapping.
    public Performance? CurrentPerformance { get; private set; }
    public Media? CurrentMedia { get; private set; }
    public string? CurrentSingerName { get; private set; }

    /// <summary>Whether the main channel is carrying an ad rather than a singer's song.</summary>
    public bool IsPlayingAd => _ads.IsPlaying;

    public PlaybackProgram CurrentProgram => _program;
    public PlaybackState State { get; private set; } = PlaybackState.Stopped;

    public TimeSpan Position
    {
        get => _clock.Position;
        private set => _clock.Position = value;
    }

    public Guid? CurrentlyPerformingUserId { get; private set; }
    public TimeSpan? StopFadeDuration { get; private set; }
    public int Pitch => _sound.Pitch;
    public int Tempo => _sound.Tempo;

    /// <summary>The named voices in the loaded file, empty for the ordinary single-track song.</summary>
    public IReadOnlyList<AudioTrack> AudioTracks => _sound.AudioTracks;

    public int LeadVolume => _sound.LeadVolume;
    public int BackingVolume => _sound.BackingVolume;
    public IReadOnlyDictionary<string, int> VoiceVolumes => _sound.VoiceVolumes;

    /// <summary>Song seconds per wall-clock second, from the open stream not the wanted tempo.</summary>
    /// <remarks>The room is still on the old rate until the stream reopens.</remarks>
    private double Rate => _rendition is { } rendition ? StreamRate.FromTempo(rendition.Tempo) : 1.0;

    public PlaybackService(
        ILogger<PlaybackService> logger,
        ISingerQueueService singerQueueService,
        IPerformanceService performanceService,
        IVenuesService venuesService,
        IAnalyticsService analytics,
        IMediaStreamService mediaStreams,
        IMediaRendererService renderers,
        IEnumerable<IDisplayProvider> displayProviders,
        IBreakMusicService breakMusic,
        IOptionsMonitor<ServiceOptions> options,
        IAudioTrackService audioTracks,
        IMediaGateService mediaGate,
        IFlashService flash,
        ITimedLyricsService timedLyrics,
        IMessageBroker broker,
        TimeProvider time)
        : base(logger)
    {
        _time = time;
        _broker = broker;
        _singerQueueService = singerQueueService;
        _performanceService = performanceService;
        _venuesService = venuesService;
        _analytics = analytics;
        _mediaStreams = mediaStreams;
        _renderers = renderers;
        _breakMusic = breakMusic;
        _optionsMonitor = options;
        _audioTracks = audioTracks;
        _mediaGate = mediaGate;
        _flash = flash;
        _timedLyrics = timedLyrics;

        _link = new DisplayLink(displayProviders, logger);
        _clock = new PlaybackClock(time, logger, () => Options.DisplayStartTimeout, () => Rate, TickAsync);
        _ads = new AdChannels(mediaStreams, _link, logger, CloseSessionAsync);
        _sound = new LiveSoundController(
            performanceService, broker, _link, logger,
            () => Options.RebuildSettleDelay,
            () => CurrentPerformance,
            () => _rendition,
            () => ReopenStreamAsync());

        foreach (var display in _link.All)
            display.PlaybackStatusChanged += OnDisplayStatusReceived;

        // The contract has no way to say either, so only the screens report them.
        foreach (var screen in _link.Screens)
        {
            screen.SongEnded += OnDisplaySongEnded;
            screen.HoldingBeforeSong += OnDisplayHolding;
        }

        // A display joining, rejoining or going away, screens included: each says so here.
        _displaySubscription = _broker.Subscribe<DisplaysChanged>(message => { _ = Task.Run(SyncDisplaySessionAsync); });

        // Words burned into the stream change only with a new encode; a display drawing them
        // itself hears the same message and re-reads them.
        _lyricSettingsSubscription = _broker.Subscribe<TimedLyricsSettingsChanged>((_, _) => AfterLyricSettingsChangeAsync());
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

    /// <summary>Whether the song has anywhere to come out — a screen, a television, anything.</summary>
    /// <remarks>Any connected display counts, not only a local screen: refusing to play with only a
    /// television attached would refuse the setup casting exists for.</remarks>
    public Task<bool> HasConnectedScreenAsync() => Task.FromResult(_link.IsConnected());

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
            await _ads.CancelAsync();

        // Before the load goes out, so a display takes the card down ahead of the song.
        _program = new PlaybackProgram.Playing(media, performance);

        // On load rather than on play: the host lines the next singer up while the room is still
        // listening to the bed, and a song that starts over the top of it is two songs at once.
        await _breakMusic.SuspendAsync();

        CurrentPerformance = performance;
        CurrentMedia = media;
        CurrentSingerName = await NameForAsync(performance);
        Position = TimeSpan.Zero;

        // After ResetState, which cleared them: a performance carries how it was sung.
        _sound.TakeFrom(performance, Options.DefaultBackingVolume);

        // Probed rather than stored: the answer costs one ffprobe, and a file replaced on disk
        // would otherwise keep whatever its tracks were called at import.
        _sound.TakeTracks(performance, await _audioTracks.ReadTracksAsync(media.FilePath));

        _sessionActivity = _analytics.StartActivity(AnalyticActivities.Session);
        _sessionActivity.SetTag("media_id", media.Id);

        await _singerQueueService.MoveUserToStartAsync(performance.SingerId);
        _singerQueueService.LockTopSlot();

        Logger.LogInformation("Loading media '{Title}' for performance {PerformanceId}", media.Title, performance.Id);

        try
        {
            // A display that draws the words loads them itself, inside this call.
            await LoadFromStartAsync(media);
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

    /// <summary>Puts back what PlayAdAsync set up before it failed.</summary>
    /// <remarks>Nothing runs the clock for an ad that never loaded, so without this the ad holds the
    /// main channel and the bed until the next button press.</remarks>
    private async Task AbandonAdAsync()
    {
        // An ad this one replaced is still on the display; the card cannot come back over it.
        await _link.SendAsync(display => display.StopAsync(TimeSpan.Zero));

        await EndedAsync();

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
        _program = new PlaybackProgram.Idle();

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
        if (!_ads.Accepts(ad, performanceLoaded: CurrentPerformance is not null))
            return false;

        ResetState();

        // Only what the room can hear takes the room. A silent still leaves the bed playing under
        // it rather than dropping the venue into fifteen seconds of nothing.
        if (ad.HasOwnAudio())
            await _breakMusic.SuspendAsync();

        CurrentMedia = ad.Visual;
        _ads.Current = ad;
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

        _program = ad.Visual switch
        {
            null => new PlaybackProgram.Idle(),
            { } video when playsOnMainChannel => new PlaybackProgram.Playing(video, null),
            { } still => new PlaybackProgram.AdStill(_mediaStreams.BuildImageUrl(still.Id), still.ImageScaling),
        };

        try
        {
            if (playsOnMainChannel)
                await LoadFromStartAsync(ad.Visual!);

            if (ad.Audio is { } audio)
                await _ads.PlayAudioAsync(audio, ad.AudioStart);
        }
        catch
        {
            await AbandonAdAsync();
            throw;
        }

        _broker.Announce(new PlaybackChanged());

        if (!playsOnMainChannel)
        {
            // A still or an audio-only spot drives no media element on the main channel, so there
            // is nothing for the play path to start: the clock alone runs it out.
            State = PlaybackState.Playing;
            _clock.Start();

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
        _clock.MarkTransportMoved();

        // Leaving Stopping here is what tells an in-flight StopAsync to abandon its completion.
        State = PlaybackState.Playing;
        StopFadeDuration = null;
        _clock.AwaitDisplayStart();
        _clock.Start();

        _analytics.RecordPlaybackStateTransition(PlaybackState.Playing);

        Logger.LogInformation("Playback started for user {UserId}", CurrentPerformance?.SingerId);

        if (supersedesStop && CurrentMedia is { } resumed)
        {
            Logger.LogInformation("Play superseded a stop; reloading the screens at {Position}", Position);

            // A replay behind the stream rebuilds it, as a reopen still in flight may be doing.
            await _reloadLock.WaitAsync();
            try
            {
                await ReplayOntoDisplayAsync(resumed, seekPast: TimeSpan.Zero);
            }
            finally
            {
                _reloadLock.Release();
            }
        }
        else
        {
            await _link.SendAsync(display => display.PlayAsync());
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
        _clock.Stop();
        Position = target;

        _clock.ReleaseHold();
        _clock.MarkTransportMoved();

        Logger.LogInformation("Seeking to {Position}", target);

        await _link.SendAsync(display => display.SeekAsync(target));

        if (wasPlaying)
            _clock.Start();

        _broker.Announce(new PlaybackChanged());
    }

    public Task SetPitchAsync(int semitones) => _sound.SetPitchAsync(semitones);

    public Task SetTempoAsync(int tempo) => _sound.SetTempoAsync(tempo);

    public Task SetLeadVolumeAsync(int volume) => _sound.SetLeadVolumeAsync(volume);

    public Task SetBackingVolumeAsync(int volume) => _sound.SetBackingVolumeAsync(volume);

    public Task SetVoiceVolumeAsync(string voice, int volume) => _sound.SetVoiceVolumeAsync(voice, volume);

    /// <summary>Rebuilds the song at the playhead when the words burned into it would now read
    /// differently, the same way a key change does.</summary>
    /// <remarks>Only for a display that asked for burned-in words and a song that has some: any
    /// other encode carries no words, and rebuilding it would be a hole in the song for nothing.</remarks>
    private async Task AfterLyricSettingsChangeAsync()
    {
        // An ad, or a song with nothing open, is left to ReopenStreamAsync to refuse, as a key change is.
        if (CurrentMedia is not { } media) return;

        if (!_link.DescribeTarget().BurnLyrics) return;

        TimedLyrics? words;
        try { words = await _timedLyrics.GetTimedLyricsAsync(media.FilePath); }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not re-read the words of '{Title}'; its stream is left as it is", media.Title);
            return;
        }

        // The test the burn-in itself makes: a timing with no pages burns nothing in.
        if (words is not { Pages.Count: > 0 } || !ReferenceEquals(media, CurrentMedia)) return;

        Logger.LogInformation("Lyric adjustments changed; rebuilding '{Title}' with its words redrawn", media.Title);

        _sound.ScheduleRebuild();
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
        _clock.Stop();

        // A report already past the Playing check would otherwise land after this and move the
        // paused playhead, or undo a display loss's park at the start.
        _clock.MarkTransportMoved();

        _analytics.RecordPlaybackStateTransition(PlaybackState.Paused);

        Logger.LogInformation("Playback paused at {Position}", Position);

        await _link.SendAsync(display => display.PauseAsync());

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
        if (fade > TimeSpan.Zero && !_link.CanFade())
            fade = TimeSpan.Zero;

        // Hold CurrentPerformance/CurrentMedia until the screens have finished fading, so the
        // UI can show the song winding down instead of blanking while it is still audible.
        _clock.Stop();
        State = PlaybackState.Stopping;
        StopFadeDuration = fade > TimeSpan.Zero ? fade : null;

        // State alone cannot tell this stop from a later one: resume then stop again inside the
        // fade puts it back in Stopping, and this wait would end the later stop's fade early.
        var stop = Interlocked.Increment(ref _stopGeneration);

        Logger.LogInformation("Playback stopping (fade={Fade})", fade);

        _broker.Announce(new PlaybackChanged());

        await _link.SendAsync(display => display.StopAsync(fade));

        if (fade > TimeSpan.Zero)
            await Task.Delay(fade, _time);

        // Play or Load during the fade supersedes this stop, and so does a later stop.
        if (State != PlaybackState.Stopping || Volatile.Read(ref _stopGeneration) != stop)
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
            foreach (var display in _link.All)
                display.PlaybackStatusChanged -= OnDisplayStatusReceived;
            foreach (var screen in _link.Screens)
            {
                screen.SongEnded -= OnDisplaySongEnded;
                screen.HoldingBeforeSong -= OnDisplayHolding;
            }
            _displaySubscription?.Dispose();
            _displaySubscription = null;
            _lyricSettingsSubscription?.Dispose();
            _lyricSettingsSubscription = null;

            // _reloadLock is deliberately not disposed: a detached sync may still be holding
            // it at shutdown, and its Release would then throw.
            _clock.Stop();
        }
    }

    /// <summary>Hands the running song to a display on a session it has not had it on.</summary>
    /// <remarks>Keyed on session rather than device: a restarted receiver and a screen that dropped
    /// and came back are the same device having forgotten everything. A display with no session
    /// at all is the other half of the same news, and may mean the song has lost its way out.</remarks>
    private async Task SyncDisplaySessionAsync()
    {
        var joined = _link.Joined;

        if (joined?.SessionId is null)
        {
            _link.ForgetSession();
            await HandleDisplayLossAsync();
            return;
        }

        await _reloadLock.WaitAsync();
        try
        {
            // Read inside the lock: DisplaysChanged is announced for discovery as well, so several
            // land close together and only one of them may claim the session.
            if (joined.SessionId is not { } session) return;

            if (!_link.TryClaimSession(session)) return;

            var media = CurrentMedia;

            // Mid-render (a load, or a song ending): whatever is rendering sends its own load, which
            // reaches this display too. Replaying here as well opens a second encode nothing
            // closes. A still or nothing at all is a picture, which the display draws for itself.
            if (_rendition is not { } rendition || media is null || MediaFormats.IsImage(media.Format)) return;

            // Stems alone reach only a display that mixes; anything else needs the encoded stream.
            var mixes = _link.DescribeTarget(joined).MixesStems;
            if (rendition.Url is not { Length: > 0 } && !(mixes && rendition.Stems.Count > 0)) return;

            // Reloading costs a spin-up; a running clock resumes the display behind the UI.
            _clock.Stop();

            Logger.LogInformation(
                "Display session is new; loading '{Title}' at {Position}", media.Title, Position);

            await ReplayOntoDisplayAsync(media, seekPast: rendition.StartOffset);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to sync a newly connected display");
        }
        finally
        {
            // Also covers the throwing path: a stopped clock under Playing freezes the UI.
            if (State == PlaybackState.Playing)
                _clock.Start(restart: false);

            _reloadLock.Release();
        }
    }

    private async Task HandleDisplayLossAsync()
    {
        await _reloadLock.WaitAsync();
        try
        {
            if (State != PlaybackState.Playing)
                return;

            if (await HasConnectedScreenAsync())
                return;

            // A deliberate Turn Off, a switch or a host shutdown all leave no display joined too,
            // and read identically from here for any display; only the screen tracks which, so a
            // plugin display's own disconnect keeps logging as unexpected.
            if (_link.DisconnectWasRequested)
                Logger.LogInformation("The display carrying the song was disconnected; parking it at the start");
            else
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
            Logger.LogError(ex, "Failed to handle a display going away");
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private void ResetState()
    {
        Interlocked.Increment(ref _songGeneration);

        _clock.Stop();
        _clock.Reset();

        _sessionActivity?.Dispose();
        _sessionActivity = null;

        CurrentlyPerformingUserId = null;

        _sound.CancelPendingRebuild();

        State = PlaybackState.Stopped;
        StopFadeDuration = null;
        Position = TimeSpan.Zero;

        _sound.Clear(Options.DefaultBackingVolume);
    }

    /// <summary>Throws when the encode will not start: there is no playback without it.</summary>
    private async Task<DisplayLoad> BuildLoadAsync(Media media, TimeSpan startOffset)
    {
        // Held, not closed: tearing down first would leave the room on buffered frames while the new one
        // spins up (or on nothing, if it fails). A failed rebuild costs the change, not the song.
        MediaRendition? built;

        // Not swallowed: without a rendition there is nothing to send, and pretending otherwise
        // leaves the room staring at a screen that never starts.
        try
        {
            // The ad path reaches here and reads zero: PlayAdAsync resets the state first.
            built = await _renderers.RenderAsync(new MediaRenderRequest
            {
                FilePath = media.FilePath,
                StartOffset = startOffset,
                Pitch = Pitch,
                Tempo = Tempo,
                Mix = _sound.CurrentMix,
                Target = _link.DescribeTarget(),
            });
        }
        catch (Exception ex) when (ex is not KHostException)
        {
            throw new KHostException(
                $"KHost couldn't prepare “{media.Title}” for the screens, so nothing was sent to them.",
                "Check the file is still on the drive, then try again.",
                "KH-STREAM-OPEN",
                ex);
        }

        // Retires what is there now, not what was there before the build: a stop closing the song
        // mid-build has already taken that one, and whoever swaps a session out closes it, once.
        var superseded = Interlocked.Exchange(ref _rendition, built);
        if (!ReferenceEquals(superseded, built))
            RetireSession(superseded?.Session);

        return DescribeStream(media);
    }

    /// <summary>Opens <paramref name="media"/> at its start and hands it to the display.</summary>
    private async Task LoadFromStartAsync(Media media)
    {
        // A rebuild past its settle may be mid-build: each retires what it replaces, and the one
        // finishing last would otherwise leave the room on the other's song.
        await _reloadLock.WaitAsync();
        try
        {
            await LoadOntoDisplayAsync(await BuildLoadAsync(media, TimeSpan.Zero));
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    /// <summary>Rebuilds since ffmpeg fixes its filter graph at start, which costs a short silence.</summary>
    private async Task ReopenStreamAsync(TimeSpan? at = null)
    {
        var song = Interlocked.Read(ref _songGeneration);

        await _reloadLock.WaitAsync();
        try
        {
            // Queued behind a load: the change was for the song before, and the new one opens as it should.
            if (Interlocked.Read(ref _songGeneration) != song)
                return;

            // A still holds no stream to rebuild, and an ad is nobody's song to transpose.
            if (CurrentMedia is not { } media || IsPlayingAd || _rendition is null)
                return;

            var position = at ?? Position;

            // Stopped first, as in SeekAsync: a tick mid-reload carries the old position past
            // the point the new stream is opening at.
            _clock.Stop();

            // Moves the playhead as well when a seek asked for this, so the bar answers the click
            // rather than waiting for the first report off the new stream.
            Position = position;

            Logger.LogInformation(
                "Reopening '{Title}' at {Position} pitched {Semitones:+#;-#;0} tempo {Tempo:+#;-#;0}%",
                media.Title, position, Pitch, Tempo);

            // Opened at the playhead, not opened at zero and seeked: the stream's own zero moves
            // with it, which is what StreamStartOffset carries to the screens.
            var load = await BuildLoadAsync(media, position);

            // Stopped or replaced while building: the encode is for a song that is gone, and handing
            // it over would put that song back on the display.
            if (Interlocked.Read(ref _songGeneration) != song)
            {
                await CloseStreamAsync();
                return;
            }

            await LoadOntoDisplayAsync(load);

            // A rendition of the whole song (stems) opens at the song's zero, not the playhead.
            if (_rendition is { } opened && position > opened.StartOffset)
                await _link.SendAsync(display => display.SeekAsync(position));

            // Re-read rather than captured before the rebuild: a host pause landing during it must
            // not be overridden by a Play the host never asked for.
            if (State != PlaybackState.Playing)
                return;

            // Set again because the old stream's reports moved the playhead during the rebuild.
            // Resumed where the new stream opens, never skipped ahead: the skip lands on segments
            // ffmpeg has not written, and the element starves. Covering the gap is the transport's
            // business — a screen keeps its old element playing until the new one has sound.
            Position = position;
            _clock.AwaitDisplayStart();

            await _link.SendAsync(display => display.PlayAsync());
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
                _clock.Start(restart: false);

            _reloadLock.Release();

            _broker.Announce(new PlaybackChanged());
        }
    }

    /// <summary>Hands the running stream to a display that has nothing loaded, and resumes it.</summary>
    /// <param name="seekPast">Where the stream itself opens; a playhead no further on needs no seek.</param>
    /// <remarks>A display that draws the words resends them inside the load, ahead of the seek.</remarks>
    private async Task ReplayOntoDisplayAsync(Media media, TimeSpan seekPast)
    {
        // A song parked at the start after a rebuild sits behind its stream, which holds nothing
        // before the playhead the rebuild opened it at: replayed as-is, the room resumes there.
        if (_rendition is { SeekableInPlace: false } open && Position < open.StartOffset)
        {
            Logger.LogInformation("Rebuilding the stream at {Position}, behind where it opens", Position);

            await LoadOntoDisplayAsync(await BuildLoadAsync(media, Position));
            seekPast = Position;
        }
        else
        {
            await LoadOntoDisplayAsync(DescribeStream(media));
        }

        var position = Position;

        if (position > seekPast)
            await _link.SendAsync(display => display.SeekAsync(position));

        // Re-read rather than captured before the awaits: a host pause or stop landing mid-replay
        // must win over a stale "was playing".
        if (State == PlaybackState.Playing)
        {
            _clock.AwaitDisplayStart();
            await _link.SendAsync(display => display.PlayAsync());
        }
    }

    private DisplayLoad DescribeStream(Media media) => new()
    {
        // Null when the display plays the parts directly and nothing was encoded for it. The two
        // are never both empty: a rendition with neither would be a song with nowhere to come from.
        StreamUrl = _rendition?.Url,
        StartOffset = _rendition?.StartOffset ?? TimeSpan.Zero,
        Tempo = _rendition?.Tempo ?? 0,
        Stems = _rendition?.Stems ?? [],
    };

    /// <remarks>Not under _reloadLock: a stop or a song's end would then wait out a rebuild's encode
    /// start or a slow receiver's load. The swap alone decides who closes the session.</remarks>
    private Task CloseStreamAsync() => CloseSessionAsync(Interlocked.Exchange(ref _rendition, null)?.Session);

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
        // encode has to stop. An orphaned ffmpeg would keep burning CPU for a song nobody plays.
        await CloseStreamAsync();

        var currentPerformance = CurrentPerformance;
        var wasAd = IsPlayingAd;

        CurrentPerformance = null;
        CurrentMedia = null;
        CurrentSingerName = null;
        _ads.Current = null;

        // A gap ad started below replaces this with its own.
        _program = new PlaybackProgram.Idle();

        if (wasAd)
        {
            Logger.LogInformation("Ad finished");

            // Handed back before the bed is restored, or break music would reclaim the channel
            // while the ad's own voiceover was still playing on it.
            await _ads.ReleaseAudioAsync();

            // No dequeue and no rotation: an ad is nobody's turn, and there is no slot to unlock
            // because none was ever locked for it.
            await _breakMusic.RestoreAsync();
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

        // The ad the gap started, not the one that ended: it was cleared above.
        if (_ads.Current is { } next)
        {
            // A silent ad (a still with no voiceover) runs over the bed, not over nothing. Starting one
            // only suspends the bed when it has its own sound, so only the ended song put it down.
            if (!next.HasOwnAudio())
                await _breakMusic.RestoreAsync();

            return;
        }

        await _breakMusic.RestoreAsync();
    }

    private Task LoadOntoDisplayAsync(DisplayLoad load)
    {
        _clock.MarkTransportMoved();
        _link.NoteLoading();

        return _link.SendAsync(display => display.LoadAsync(load));
    }

    /// <summary>Follows the position the display reached, not one the host asserts.</summary>
    /// <remarks>An HLS stream starts on a segment boundary, so an asserted clock is off from the
    /// first frame, and a receiver buffers seconds, so a free-running one ends a song too soon and
    /// rotates the singer away while the room still hears it. The timer only interpolates between
    /// these reports.</remarks>
    private void OnDisplayStatusReceived(object? sender, DisplayPlaybackStatus status)
    {
        if (State != PlaybackState.Playing || !status.IsPlaying) return;

        // The display carrying the song defines its clock; a report from any other is stale.
        if (!_link.IsCarrying(sender)) return;

        _clock.FollowReport(status);
    }

    /// <summary>The screen is holding the song back before its start: the playhead stays at the
    /// song's zero until a report says the song itself is running.</summary>
    /// <remarks>State stays Playing, since the room is being led in, not paused.</remarks>
    private void OnDisplayHolding(object? sender, DisplayPlaybackStatus status)
    {
        if (State != PlaybackState.Playing) return;
        if (!_link.IsCarrying(sender)) return;

        _clock.Hold(status);
    }

    /// <summary>The display played the song out: concluded here as well as on the clock, whichever
    /// comes first, since a stream that stops short of the media's duration never runs the clock out.
    /// </summary>
    private void OnDisplaySongEnded(object? sender, DisplayPlaybackStatus status)
    {
        if (State != PlaybackState.Playing) return;
        if (!_link.IsCarrying(sender)) return;

        if (!_clock.TryTakeEnd(status, out var generation)) return;

        DeferConclusion(async () =>
        {
            try
            {
                await ConcludeAsync(generation, "the display reached the end");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to conclude the song the display finished");
            }
        });
    }

    /// <summary>Retires the song that was playing under <paramref name="generation"/>, once.</summary>
    private async Task ConcludeAsync(long generation, string why)
    {
        if (!_clock.TryClaim(generation)) return;

        // As a stop does, so the song's last frame comes down; unfaded, the song being over.
        // Before EndedAsync, which may start a gap ad this would otherwise stop.
        await _link.SendAsync(display => display.StopAsync(TimeSpan.Zero));

        ResetState();

        Logger.LogInformation("Playback concluded: {Why}", why);

        await EndedAsync();

        _broker.Announce(new PlaybackChanged());
    }

    internal async Task TickAsync()
    {
        if (!_clock.TryAdvance(out var generation)) return;

        if (HasPlaybackEnded())
        {
            await ConcludeAsync(generation, "the clock reached the media's duration");
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
        var duration = _ads.Current is { } ad ? ad.Duration : CurrentMedia?.Duration;

        return duration is { } d && Position >= d;
    }

    private static class AnalyticActivities
    {
        public const string Session = "playback.session";
    }
}
