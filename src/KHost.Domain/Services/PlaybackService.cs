using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services;

public class PlaybackService : BaseService, IPlaybackService
{
    public sealed class ServiceOptions
    {
        public const string SectionName = "Playback";

        /// <summary>How long screens fade audio and video out for on stop. Zero stops instantly.</summary>
        public TimeSpan StopFadeDuration { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>Must cover the slowest screen's command latency.</summary>
        public TimeSpan SyncStartLead { get; set; } = TimeSpan.FromMilliseconds(400);
    }

    private const int ClockIntervalMs = 500;

    // Swapping _timer is a two-step read-then-assign reached from the UI dispatcher (PlayAsync,
    // SeekAsync) and from Task.Run continuations (a screen connecting) at once. Unguarded, both
    // callers assign and the loser's Timer is unreachable — it ticks, and drives a second position
    // clock and a second round of panel queries, until the process ends.
    private readonly Lock _clockLock = new();
    private Timer? _timer;

    // A tick that outruns the interval must not overlap the next: two in flight both see the song
    // ended and rotate the singer away twice.
    private int _ticking;

    private DateTime _lastTick;

    // Bounded independently of report rate: a chatty screen must not become a command storm.
    private static readonly TimeSpan ReanchorInterval = TimeSpan.FromSeconds(1);
    private DateTime _lastReanchorUtc;
    private bool _resumeWhenScreenReturns;

    // Connect and disconnect both arrive as fire-and-forget continuations, so without this they
    // can interleave and a reconnect's resume races the disconnect's pause.
    private readonly SemaphoreSlim _screenSyncLock = new(1, 1);

    private IAnalyticsActivity? _sessionActivity;

    // The host-side transcode currently feeding the screens, if any.
    private MediaStreamSession? _stream;

    // An ad's own audio track, which borrows the background channel from break music.
    private MediaStreamSession? _adAudioStream;

    // An ad's length, which is the composition's rather than any one file's.
    private TimeSpan? _adDuration;

    private readonly ISingerQueueService _singerQueueService;
    private readonly IPerformanceService _performanceService;
    private readonly IVenuesService _venuesService;
    private readonly IAnalyticsService _analytics;
    private readonly IScreenServer _screenServer;
    private readonly IMediaStreamService _mediaStreams;
    private readonly IScreenCoordinationService _screenCoordination;
    private readonly ICastService _cast;
    private readonly IBreakMusicService _breakMusic;
    private readonly IMediaService _mediaService;
    private readonly ServiceOptions _options;

    public event EventHandler? PositionChanged;
    public event EventHandler<PerformanceEndedEventArgs>? PerformanceEnded;

    public Performance? CurrentPerformance { get; private set; }
    public Media? CurrentMedia { get; private set; }

    /// <summary>Whether the main channel is carrying an ad rather than a singer's song.</summary>
    public bool IsPlayingAd { get; private set; }
    public PlaybackState State { get; private set; } = PlaybackState.Stopped;
    public TimeSpan Position { get; private set; }
    public Guid? CurrentlyPerformingUserId { get; private set; }
    public TimeSpan? StopFadeDuration { get; private set; }

    public PlaybackService(
        ILogger<PlaybackService> logger,
        ISingerQueueService singerQueueService,
        IPerformanceService performanceService,
        IVenuesService venuesService,
        IAnalyticsService analytics,
        IScreenServer screenServer,
        IMediaStreamService mediaStreams,
        IScreenCoordinationService screenCoordination,
        ICastService cast,
        IBreakMusicService breakMusic,
        IMediaService mediaService,
        IOptions<ServiceOptions> options)
        : base(logger)
    {
        _singerQueueService = singerQueueService;
        _performanceService = performanceService;
        _venuesService = venuesService;
        _analytics = analytics;
        _screenServer = screenServer;
        _mediaStreams = mediaStreams;
        _screenCoordination = screenCoordination;
        _cast = cast;
        _breakMusic = breakMusic;
        _mediaService = mediaService;
        _options = options.Value;

        _screenServer.ScreenConnected += OnScreenConnected;
        _screenServer.ScreenDisconnected += OnScreenDisconnected;
        _screenServer.StateReceived += OnScreenStateReceived;
        _cast.PlaybackStatusChanged += OnCastStatusReceived;
    }

    public async Task<bool> HasConnectedScreenAsync()
    {
        // A Cast receiver is not a screen, but it is somewhere the song comes out — refusing to
        // play with only a television attached is refusing the setup casting exists for.
        if (_cast.ConnectedDeviceId is { Length: > 0 }) return true;

        try
        {
            await foreach (var _ in _screenServer.GetConnectedScreensAsync())
                return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to enumerate connected screens");
        }

        return false;
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

        ResetState();

        IsPlayingAd = false;

        // The venue card is only for when nothing is playing.
        await SendToScreensAsync(new HideImageCommand());

        // On load rather than on play: the host lines the next singer up while the room is still
        // listening to the bed, and a song that starts over the top of it is two songs at once.
        await _breakMusic.SuspendAsync();

        CurrentPerformance = performance;
        CurrentMedia = media;
        Position = TimeSpan.Zero;

        _sessionActivity = _analytics.StartActivity(AnalyticActivities.Session);
        _sessionActivity.SetTag("media_id", media.Id);

        await _singerQueueService.MoveUserToStartAsync(performance.SingerId);
        _singerQueueService.LockTopSlot();

        Logger.LogInformation("Loading media '{Title}' for performance {PerformanceId}", media.Title, performance.Id);

        await SendToScreensAsync(await BuildLoadCommandAsync(media, TimeSpan.Zero));

        InvokeStateChanged();
    }

    /// <summary>
    /// Plays media on the main channel that is nobody's turn. It ends without dequeuing or
    /// rotating: retiring a singer for an ad would cost them the song they are still waiting on.
    /// </summary>
    /// <summary>The simple case: one file, its own length, whatever audio it carries.</summary>
    public Task<bool> PlayAdAsync(Media media) => PlayAdAsync(new AdPlayback
    {
        Visual = media,
        Duration = media.Duration ?? TimeSpan.Zero,
    });

    public async Task<bool> PlayAdAsync(AdPlayback ad)
    {
        if (ad.IsEmpty)
        {
            Logger.LogWarning("Ad refused: nothing to show and nothing to play");
            return false;
        }

        // Nothing here is watching an ad the way a host watches a song, and the clock ends one by
        // its duration — without one this would hold the main channel all night.
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
        if (ad.HasOwnAudio)
            await _breakMusic.SuspendAsync();

        CurrentMedia = ad.Visual;
        _adDuration = ad.Duration;
        IsPlayingAd = true;
        Position = TimeSpan.Zero;

        if (!await HasConnectedScreenAsync())
        {
            Logger.LogWarning("Ad refused: no screens are connected");

            await EndedAsync();
            InvokeStateChanged();
            return false;
        }

        Logger.LogInformation("Playing ad '{Title}'", ad.Visual?.Title ?? ad.Audio?.Title);

        var playsOnMainChannel = ad.Visual is { } v && !MediaFormats.IsImage(v.Format);

        if (playsOnMainChannel)
        {
            await SendToScreensAsync(new HideImageCommand());
            await SendToScreensAsync(await BuildLoadCommandAsync(ad.Visual!, TimeSpan.Zero));
        }
        else if (ad.Visual is { } still)
        {
            await SendToScreensAsync(new ShowImageCommand { Url = _mediaStreams.BuildImageUrl(still.Id) });
        }

        // Opened at the offset rather than trimmed: a clip out of a longer file costs no re-encode,
        // and the host clock stops it at the ad's duration.
        if (ad.Audio is { } audio)
        {
            _adAudioStream = await _mediaStreams.OpenAsync(audio.FilePath, ad.AudioStart);

            await SendToAudioScreenAsync(new LoadBackgroundCommand
            {
                StreamUrl = _adAudioStream.PlaylistUrl,
                AutoPlay = true,
            });
        }

        InvokeStateChanged();

        if (!playsOnMainChannel)
        {
            // A still or an audio-only spot drives no media element on the main channel, so there
            // is nothing for the play path to start — the clock alone runs it out.
            State = PlaybackState.Playing;
            StartClock();

            InvokeStateChanged();
            return true;
        }

        await PlayAsync();

        if (State == PlaybackState.Playing)
            return true;

        // Refused — with no screen there is nothing to run the clock down and clear this, so the
        // ad would hold the main channel and keep the bed suspended behind it.
        await EndedAsync();

        InvokeStateChanged();

        return false;
    }

    public async Task PlayAsync()
    {
        // Media rather than performance: an ad is loaded the same way and has no singer.
        if (CurrentMedia is null || State == PlaybackState.Playing) return;

        // Playing during a stop's fade supersedes it, but the screens have already been told to
        // fade out and drop the media — so they need it handed back before being told to play.
        var supersedesStop = State == PlaybackState.Stopping;

        // Without a screen there is no audio or video, but the position timer would still run
        // the performance to completion and rotate the singer away — burning their turn.
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

            await SendToScreensAsync(DescribeStream(resumed));

            if (Position > TimeSpan.Zero)
                await SendToScreensAsync(new SeekCommand { Position = Position });
        }

        await SendToScreensAsync(new PlayCommand());
        await CastAsync(c => c.PlayAsync());

        // After the play command, so a synced screen already has the stream open when it is told
        // which instant to start on.
        await PublishTimelineAsync(isPlaying: true, scheduleAhead: true);

        InvokeStateChanged();
    }

    public async Task SeekAsync(TimeSpan position)
    {
        // Loaded is enough. A song sits in Stopped until Play, and scrubbing before starting it —
        // to skip a long intro — is exactly when a host reaches for the bar.
        if (CurrentMedia is not { } media)
            return;

        var target = Clamp(position, media.Duration);
        var wasPlaying = State == PlaybackState.Playing;

        // Stopped first: the tick would otherwise carry the old position over the new one.
        StopClock();
        Position = target;

        Logger.LogInformation("Seeking to {Position}", target);

        await SendToScreensAsync(new SeekCommand { Position = target });
        await CastAsync(c => c.SeekAsync(target));

        // Screens play to a scheduled instant rather than following us, so moving the playhead has
        // to re-anchor the whole group — not only the screen the host happens to be looking at.
        await PublishTimelineAsync(wasPlaying, target, scheduleAhead: wasPlaying);

        if (wasPlaying)
            StartClock();

        InvokeStateChanged();
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

        await SendToScreensAsync(new PauseCommand());
        await CastAsync(c => c.PauseAsync());
        await PublishTimelineAsync(isPlaying: false);

        InvokeStateChanged();

        return;
    }

    public async Task StopAsync()
    {
        if (State == PlaybackState.Stopping)
            return;

        // Screens can only fade while frames are flowing, so a paused stop is instant on the
        // screen — showing "Fading out…" here would just stall the UI over a blanked screen.
        var fade = State == PlaybackState.Playing ? _options.StopFadeDuration : TimeSpan.Zero;

        // Hold CurrentPerformance/CurrentMedia until the screens have finished fading, so the
        // UI can show the song winding down instead of blanking while it is still audible.
        StopClock();
        State = PlaybackState.Stopping;
        StopFadeDuration = fade > TimeSpan.Zero ? fade : null;

        Logger.LogInformation("Playback stopping (fade={Fade})", fade);

        InvokeStateChanged();

        await SendToScreensAsync(new StopCommand { FadeDuration = fade });
        await CastAsync(c => c.StopAsync());

        if (fade > TimeSpan.Zero)
            await Task.Delay(fade);

        // Play or Load during the fade supersedes this stop.
        if (State != PlaybackState.Stopping)
            return;

        _analytics.RecordPlaybackStateTransition(PlaybackState.Stopped);

        ResetState();

        Logger.LogInformation("Playback stopped");

        await EndedAsync();

        InvokeStateChanged();
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
            _cast.PlaybackStatusChanged -= OnCastStatusReceived;

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

    /// <summary>A screen joining mid-session has nothing loaded, so PlayCommand alone is rejected.</summary>
    private async Task SyncNewScreenAsync()
    {
        await _screenSyncLock.WaitAsync();
        try
        {
            var media = CurrentMedia;

            // A still sits on the screen rather than in a stream, so there is nothing to reload —
            // and reloading would try to open a transcode for a picture. It just has to be put up
            // again on the screen that has arrived. Keyed on the format rather than on it being an
            // ad: what matters is that the thing on screen is a picture, whatever put it there.
            if (media is not null && MediaFormats.IsImage(media.Format))
            {
                await SendToScreensAsync(new ShowImageCommand { Url = _mediaStreams.BuildImageUrl(media.Id) });
                return;
            }

            // Nothing is playing, so the joiner gets the venue's card rather than the bare
            // placeholder. An audio-only ad lands here too: it holds no picture of its own, so the
            // card is exactly what should be on screen behind it.
            if (media is null)
            {
                await ShowIdleCardAsync();
                return;
            }

            // A screen reconnecting under the same id overwrites its own tracked connection, so
            // the stale disconnect is discarded and nothing is pending. Resume on either signal.
            bool resume = State == PlaybackState.Playing
                || (_resumeWhenScreenReturns && State == PlaybackState.Paused);
            _resumeWhenScreenReturns = false;

            // Reloading costs an ffmpeg spin-up; a running clock resumes the screen behind the UI.
            StopClock();

            var position = Position;

            Logger.LogInformation("Screen connected; reloading '{Title}' at {Position}", media.Title, position);

            // Reuse the transcode that is already running rather than starting a second one —
            // one host stream feeding every screen is the whole point of moving ffmpeg here.
            await SendToScreensAsync(_stream is not null
                ? DescribeStream(media)
                : await BuildLoadCommandAsync(media, TimeSpan.Zero));

            if (position > TimeSpan.Zero)
            {
                await SendToScreensAsync(new SeekCommand { Position = position });
                await CastAsync(c => c.SeekAsync(position));
            }

            if (!resume)
            {
                // A screen that joins a paused song still needs to know where the group is parked.
                await PublishTimelineAsync(isPlaying: false, position);
                return;
            }

            // PlayAsync is a no-op while State is already Playing, so drive the screen directly
            // there and keep it for the paused case, which still needs the state transition.
            if (State == PlaybackState.Playing)
            {
                await SendToScreensAsync(new PlayCommand());

                // Re-anchoring moves the whole group, not just the joiner — which is correct: the
                // reload froze the clock, so every screen has to be told where the song now is.
                await PublishTimelineAsync(isPlaying: true, position, scheduleAhead: true);
            }
            else
            {
                await PlayAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to sync a newly connected screen");
        }
        finally
        {
            // Also covers the throwing path: a stopped clock under Playing freezes the UI.
            if (State == PlaybackState.Playing)
                EnsureClockRunning();

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

            var behavior = await GetScreenDisconnectBehaviorAsync();

            Logger.LogWarning("Last screen disconnected mid-performance; applying {Behavior}", behavior);

            switch (behavior)
            {
                case ScreenDisconnectBehavior.CancelPerformance:
                    ResetState();
                    await EndedAsync();
                    InvokeStateChanged();
                    break;

                case ScreenDisconnectBehavior.RestartFromStart:
                    await PauseAsync();
                    Position = TimeSpan.Zero;
                    InvokeStateChanged();
                    break;

                default:
                    _resumeWhenScreenReturns = true;
                    await PauseAsync();
                    break;
            }
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

    private void StartClock()
    {
        lock (_clockLock)
        {
            _lastTick = DateTime.UtcNow;
            _timer?.Dispose();
            _timer = new Timer(OnTick, null, ClockIntervalMs, ClockIntervalMs);
        }
    }

    /// <summary>Starts the clock only if it is not already running, as one indivisible step.</summary>
    private void EnsureClockRunning()
    {
        lock (_clockLock)
        {
            if (_timer is not null) return;

            _lastTick = DateTime.UtcNow;
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

    private async Task<ScreenDisconnectBehavior> GetScreenDisconnectBehaviorAsync()
    {
        var venue = await _venuesService.ReadSelectedVenueAsync();
        return venue?.Settings.OnScreenDisconnect ?? ScreenDisconnectBehavior.ResumeOnReconnect;
    }

    private void ResetState()
    {
        StopClock();

        _sessionActivity?.Dispose();
        _sessionActivity = null;

        CurrentlyPerformingUserId = null;
        _resumeWhenScreenReturns = false;

        State = PlaybackState.Stopped;
        StopFadeDuration = null;
        Position = TimeSpan.Zero;
    }

    /// <summary>Throws when the transcode will not start: there is no playback without it.</summary>
    private async Task<LoadMediaCommand> BuildLoadCommandAsync(Media media, TimeSpan startOffset)
    {
        await CloseStreamAsync();

        // Not swallowed: every screen plays the host's stream, so without one there is nothing to
        // send and pretending otherwise leaves the room staring at a screen that never starts.
        try
        {
            _stream = await _mediaStreams.OpenAsync(media.FilePath, startOffset);
        }
        catch (Exception ex)
        {
            throw new KHostException(
                $"KHost couldn't prepare \u201c{media.Title}\u201d for the screens, so nothing was sent to them.",
                "Check the file is still on the drive, then try again.",
                "KH-STREAM-OPEN",
                ex);
        }

        var command = DescribeStream(media);

        if (command.StreamUrl is { Length: > 0 } url)
            await CastAsync(c => c.LoadAsync(url, command.StreamStartOffset));

        return command;
    }

    private LoadMediaCommand DescribeStream(Media media) => new()
    {
        StreamUrl = _stream?.PlaylistUrl
            ?? throw new InvalidOperationException($"No host stream is open for '{media.Title}'."),
        StreamStartOffset = _stream?.StartOffset ?? TimeSpan.Zero,
    };

    private async Task CloseAdAudioStreamAsync()
    {
        var stream = _adAudioStream;
        _adAudioStream = null;

        if (stream is null) return;

        try { await _mediaStreams.CloseAsync(stream.Id); }
        catch (Exception ex) { Logger.LogWarning(ex, "Failed to close ad audio stream {SessionId}", stream.Id); }
    }

    /// <summary>The bed and an ad's own audio share one channel, and only the audible screen gets it.</summary>
    private async Task SendToAudioScreenAsync(IScreenCommand command)
    {
        try
        {
            var screenId = await _screenCoordination.EnsureRolesAsync();

            if (screenId is null) return;

            await _screenServer.SendCommandAsync(screenId, command);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to send {Command} to the audio screen", command.GetType().Name);
        }
    }

    private async Task CloseStreamAsync()
    {
        var stream = _stream;
        _stream = null;

        if (stream is null) return;

        try { await _mediaStreams.CloseAsync(stream.Id); }
        catch (Exception ex) { Logger.LogWarning(ex, "Failed to close media stream {SessionId}", stream.Id); }
    }

    private async Task EndedAsync()
    {
        // Every path that finishes a performance runs through here, so it is the one place the
        // transcode has to stop — an orphaned ffmpeg would keep burning CPU for a song nobody plays.
        await CloseStreamAsync();

        var currentPerformance = CurrentPerformance;
        var wasAd = IsPlayingAd;

        CurrentPerformance = null;
        CurrentMedia = null;
        IsPlayingAd = false;

        if (wasAd)
        {
            Logger.LogInformation("Ad finished");

            _adDuration = null;

            // Handed back before the bed is restored, or break music would reclaim the channel
            // while the ad's own voiceover was still playing on it.
            if (_adAudioStream is not null)
            {
                await SendToAudioScreenAsync(new StopBackgroundCommand());
                await CloseAdAudioStreamAsync();
            }

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

        // Something took the gap. The bed and the card come back when that ends instead.
        if (IsPlayingAd)
            return;

        await _breakMusic.RestoreAsync();
        await ShowIdleCardAsync();
    }

    /// <summary>
    /// Puts the venue's card up now that nothing is playing. Hides whatever was there when the
    /// venue has set none, so an ad's still never outlives the ad.
    /// </summary>
    private async Task ShowIdleCardAsync()
    {
        try
        {
            var venue = await _venuesService.ReadSelectedVenueAsync();
            var brandingId = venue?.Settings.BrandingImageMediaId;

            if (brandingId is null)
            {
                await SendToScreensAsync(new HideImageCommand());
                return;
            }

            var media = await _mediaService.ReadAsync(brandingId.Value);

            if (media is null || !MediaFormats.IsImage(media.Format))
            {
                await SendToScreensAsync(new HideImageCommand());
                return;
            }

            await SendToScreensAsync(new ShowImageCommand { Url = _mediaStreams.BuildImageUrl(media.Id) });
        }
        catch (Exception ex)
        {
            // Never fatal: the card is decoration, and failing to draw it must not stop the queue
            // moving on to the next singer.
            Logger.LogWarning(ex, "Could not show the venue card");
        }
    }

    /// <summary>
    /// Separate from the screens on purpose: a receiver holds no role and must not inherit
    /// whatever the screens gain next.
    /// </summary>
    private async Task CastAsync(Func<ICastService, Task> action)
    {
        if (_cast.ConnectedDeviceId is not { Length: > 0 }) return;

        try { await action(_cast); }
        catch (Exception ex) { Logger.LogWarning(ex, "Failed to drive the Cast receiver"); }
    }

    private async Task SendToScreensAsync(IScreenCommand command)
    {
        try
        {
            await _screenServer.BroadcastCommandAsync(command);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to send {Command} to screens", command.GetType().Name);
        }
    }

    /// <summary>A start is anchored ahead so every synced screen begins on the same instant.</summary>
    private async Task PublishTimelineAsync(bool isPlaying, TimeSpan? position = null, bool scheduleAhead = false)
    {
        var anchor = DateTime.UtcNow + (scheduleAhead ? _options.SyncStartLead : TimeSpan.Zero);

        await PublishTimelineAsync(position ?? Position, anchor, isPlaying);
    }

    private async Task PublishTimelineAsync(TimeSpan position, DateTime anchorUtc, bool isPlaying)
    {
        try
        {
            var screens = await SyncCapableScreensAsync();
            if (screens.Count == 0) return;

            await _screenCoordination.EnsureRolesAsync();
            var primaryScreenId = _screenCoordination.PrimaryScreenId;

            // Addressed per screen rather than broadcast: a loose consumer cannot honour a
            // schedule, and sending it one would only invite it to try.
            foreach (var screen in screens)
                await _screenServer.SendCommandAsync(screen.ScreenId, new SetTimelineCommand
                {
                    Position = position,
                    AnchorUtc = anchorUtc,
                    IsPlaying = isPlaying,
                    IsPrimary = screen.ScreenId == primaryScreenId,
                });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to publish the timeline to synced screens");
        }
    }

    /// <summary>
    /// Anchors on a position a real screen reached. Asserting one instead holds the followers to
    /// a clock they cannot reach — an HLS seek lands on a segment boundary, so they never arrive.
    /// </summary>
    private void OnScreenStateReceived(object? sender, ScreenStateReceivedEventArgs e)
    {
        if (State != PlaybackState.Playing) return;
        if (e.ScreenId != _screenCoordination.PrimaryScreenId) return;
        if (e.State is not ScreenPlaybackState state) return;
        if (!state.IsPlaying || state.SampledAtUtc is not { } sampledAt) return;

        // The primary defines the song position, so the host's own clock follows it rather than
        // free-running — the timer is only an interpolator between these reports now.
        var now = DateTime.UtcNow;

        // The position always follows the primary; only the republish is rate-limited.
        Position = state.Position + (now - sampledAt);
        _lastTick = now;

        if (now - _lastReanchorUtc < ReanchorInterval) return;
        _lastReanchorUtc = now;

        _ = PublishTimelineAsync(state.Position, sampledAt, isPlaying: true);
    }

    /// <summary>
    /// A receiver buffers seconds, so a free-running timer ends the song — and rotates the singer
    /// away — while the room is still hearing it.
    /// </summary>
    private void OnCastStatusReceived(object? sender, CastPlaybackStatus status)
    {
        if (State != PlaybackState.Playing || !status.IsPlaying) return;

        // A real primary is the better clock: its reports are timestamped against a measured
        // offset, where a receiver's are only timestamped on arrival.
        if (_screenCoordination.PrimaryScreenId is not null) return;

        Position = status.Position + (DateTime.UtcNow - status.SampledAtUtc);
        _lastTick = DateTime.UtcNow;
    }

    private async Task<List<IScreenConnection>> SyncCapableScreensAsync()
    {
        var screens = new List<IScreenConnection>();

        try
        {
            await foreach (var screen in _screenServer.GetConnectedScreensAsync())
                if (screen.Capabilities.SupportsSync)
                    screens.Add(screen);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to enumerate synced screens");
        }

        return screens;
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
        var now = DateTime.UtcNow;
        Position += now - _lastTick;
        _lastTick = now;

        if (HasPlaybackEnded())
        {
            ResetState();

            Logger.LogInformation("Playback concluded");

            await EndedAsync();

            InvokeStateChanged();
            return;
        }

        // Not StateChanged: the queue panels answer that one with a queue read and a media read
        // per singer, and at twice a second for a whole night that is the console's entire load.
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool HasPlaybackEnded()
    {
        // An ad runs for the length its playlist entry gave it, which is not any one file's: a
        // still has a default, and a voiceover may be a clip out of something far longer.
        var duration = IsPlayingAd ? _adDuration : CurrentMedia?.Duration;

        return duration is { } d && Position >= d;
    }

    private static class AnalyticActivities
    {
        public const string Session = "playback.session";
    }
}
