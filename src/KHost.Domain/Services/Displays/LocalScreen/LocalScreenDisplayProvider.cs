using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common.Display;
using KHost.Common.Media;
using KHost.Domain.Services.Visualisations;
using KHost.IPC.SignalR.Contracts;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QRCoder;

namespace KHost.Domain.Services.Displays.LocalScreen;

/// <summary>The local screen app, reached the same way every other display is.</summary>
/// <remarks>Core, not a plugin: this is the host's own transport, registered by the host.
/// <c>PluginLoader</c> must not bind it and it must never appear on the Plugins page — it simply
/// travels the path a plugin's display travels, so <c>PlaybackService</c> has one kind of thing to
/// drive.
///
/// <para>It owns everything the screen shows, not only the song: the marquee (composed here from the
/// venue's settings and <c>IUpNextService</c>), the QR codes, the break music card, the venue's card
/// or an ad's still, the song's timed words and the intro card ahead of them. None of that is on
/// <c>IDisplayProvider</c>, which is transport only. The host announces what moved and this pulls the whole current state of whatever that
/// message drives, so a screen that connects is sent everything afresh rather than a replay of what
/// it missed. It reads only what a plugin's display could read; encoding and the screen's commands
/// are its own.</para>
///
/// <para>Discovery here is not a sweep. There is nothing to find on a machine: starting discovery
/// launches a screen and it registers back, which is why <see cref="IsDiscovering"/> reports the
/// launch rather than a search.</para></remarks>
public sealed class LocalScreenDisplayProvider : IDisplayProvider, IStartsWithTheHost, IDisposable
{
    /// <summary>The id a launched screen is given. One at a time, so one name is enough.</summary>
    internal const string LocalScreenId = "Screen 1";

    /// <summary>Full level, what a screen is sent on connect; the room's mixer sets the real one.</summary>
    private const float FullVolume = 1.0f;

    /// <summary>One module of white: the standard four-module border reads as a slab over video.</summary>
    private const int DefaultSafeZone = 1;

    /// <summary>Almost flush, as a fraction of the shorter side: overscan crops a flush edge. Shared by
    /// everything in a corner, since the inset belongs to the corner and not to what sits in it.</summary>
    private const double DefaultOffset = 0.2;

    /// <summary>Bottom-left, away from the code's default corner, so the two stack when unset.</summary>
    private const OverlayCorner DefaultBreakMusicCardCorner = OverlayCorner.BottomLeft;

    /// <summary>A screen takes seconds to register once launched; this is how long ConnectAsync
    /// waits for it before reporting a launch that never came back rather than a refusal.</summary>
    private static readonly TimeSpan DefaultRegistrationTimeout = TimeSpan.FromSeconds(10);

    /// <summary>One host action announces from several services in turn — a stop is the playback,
    /// the dequeue and the rotation — and a redraw read between two of them draws a queue that
    /// never existed. Long enough to span that run, short enough that nobody sees it.</summary>
    private static readonly TimeSpan DefaultRedrawSettle = TimeSpan.FromMilliseconds(50);

    private readonly IScreenServer _screenServer;
    private readonly IReadOnlyList<IScreenProvider> _launchers;
    private readonly IVenuesService? _venuesService;
    private readonly IMessageBroker _broker;
    private readonly SubscriptionSet _subscriptions = new();
    private readonly ILogger<LocalScreenDisplayProvider> _logger;

    // Resolved on use, never in the constructor: playback, and the services behind the marquee and
    // break music, take every IDisplayProvider, this one included.
    private readonly IServiceProvider? _services;

    // Held as the monitor, not a value: App Settings applies without a restart.
    private readonly IOptionsMonitor<PlaybackService.ServiceOptions>? _playbackOptions;

    // Serialises picture draws, which arrive from the load path and from several detached redraws.
    private readonly SemaphoreSlim _pictureLock = new(1, 1);

    /// <summary>The program the screen's picture was last drawn for; null until anything was.</summary>
    private PlaybackProgram? _pictured;

    // Serialises lyric sends: a load can come from a song starting and a screen rejoining at once.
    private readonly SemaphoreSlim _lyricsLock = new(1, 1);

    /// <summary>The song the words were read for, by identity: a rebuild reloads the same program.</summary>
    private PlaybackProgram.Playing? _lyricsFor;
    private TimedLyrics? _lyrics;

    /// <summary>The session the words last went to; a screen on a newer one holds none of them.</summary>
    private Guid? _lyricsSentOn;
    private readonly TimeSpan _registrationTimeout;

    // Serialises visualiser sends: a load and a venue edit can both decide it at once.
    private readonly SemaphoreSlim _visualiserLock = new(1, 1);

    /// <summary>The song the visualiser was decided for, by identity, and the playlist entry picked
    /// for it: a rebuild or a rejoin reloads the same program and must keep the same picture.</summary>
    private PlaybackProgram.Playing? _visualiserFor;
    private (Guid PlaylistId, Guid EntryId)? _visualiserPick;
    private DisplayLoad? _visualiserLoad;

    /// <summary>The probe's answer for <see cref="_visualiserFor"/>, asked once per song.</summary>
    private bool? _visualiserSourceHasPicture;

    /// <summary>Where <see cref="_visualiserFor"/>'s levels are served, once a read was started.
    /// Kept across a key change's reload: the levels are indexed by song time.</summary>
    private string? _visualiserLevelsUrl;

    /// <summary>The video entry's URL for <see cref="_visualiserFor"/>, null when it cannot be had; asked
    /// once per song and video, since the first ask may encode for seconds.</summary>
    private (Guid MediaId, string? Url)? _visualiserVideo;

    /// <summary>The video being resolved off the load path for <see cref="_visualiserFor"/>.</summary>
    private Guid? _visualiserVideoPending;

    // Every venue edit and every song redraws the codes, and the picture only changes when the
    // payload does. Encoding a few times a minute for an unchanged string is work for nothing.
    private readonly Dictionary<string, (string Image, int Modules)> _encoded = [];

    // What each overlay last put on this session's screen, as sent. Keyed to the session because
    // a screen that registers again holds nothing and must be sent all of it, unchanged or not.
    private readonly Dictionary<Type, string> _overlaysSent = [];
    private Guid? _overlaysSentOn;

    private readonly TimeSpan _redrawSettle;
    private readonly Lock _redrawGate = new();
    private Overlay _redrawOwed;
    private bool _redrawRunning;

    private bool _launching;

    /// <summary>Answered by <see cref="OnScreenConnected"/> once the launch this ConnectAsync
    /// started actually registers. Not reset to null afterwards: a stale completed source left
    /// here is harmless, and clearing it would race a ConnectAsync that just replaced it.</summary>
    private volatile TaskCompletionSource<bool>? _registering;

    /// <summary>The screen that is up, tracked from the events rather than read back.</summary>
    /// <remarks>This is load-bearing, not an optimisation. <c>ConnectedDeviceId</c> and
    /// <c>Devices</c> are read during a Blazor render, and the server raises
    /// <c>ScreenDisconnected</c> while holding the very lock a read back would wait on. Blocking
    /// on it from the renderer's dispatcher deadlocks the circuit outright — the console goes
    /// blank and never recovers, with nothing thrown to say why. The events carry the connection,
    /// so nothing has to be asked for. Each registration gets its own session id, so a screen that
    /// comes back is known to hold nothing even under the same connection.</remarks>
    private volatile ScreenSession? _connected;

    /// <summary>Set by <see cref="NotifyDisconnectRequested"/>; read by <see cref="DisconnectWasRequested"/>.
    /// Cleared on the next registration, so a later unexpected drop is not blamed on an old request.</summary>
    private volatile bool _disconnectRequested;

    /// <summary>The stream the last load pointed the screen at, empty for a stems-only load, as the
    /// screen's reports spell it. An end reported against any other is the old stream's.</summary>
    private volatile string _loadedStreamUrl = string.Empty;

    /// <summary>Whether a performance was under way at the last PlaybackChanged.</summary>
    private bool _performanceUnderWay;

    public LocalScreenDisplayProvider(
        ILogger<LocalScreenDisplayProvider> logger,
        IScreenServer screenServer,
        IEnumerable<IScreenProvider> launchers,
        IMessageBroker broker,
        IVenuesService? venuesService = null,
        TimeSpan? registrationTimeout = null,
        IServiceProvider? services = null,
        TimeSpan? redrawSettle = null,
        IOptionsMonitor<PlaybackService.ServiceOptions>? playbackOptions = null)
    {
        _logger = logger;
        _screenServer = screenServer;
        _launchers = [.. launchers];
        _broker = broker;
        _venuesService = venuesService;
        _registrationTimeout = registrationTimeout ?? DefaultRegistrationTimeout;
        _services = services;
        _redrawSettle = redrawSettle ?? DefaultRedrawSettle;
        _playbackOptions = playbackOptions;

        _screenServer.ScreenConnected += OnScreenConnected;
        _screenServer.ScreenDisconnected += OnScreenDisconnected;
        _screenServer.StateReceived += OnStateReceived;

        // The venue owns how the marquee, the codes and the card look, including whether each is there
        // at all.
        _subscriptions.Add(broker.Subscribe<SelectedVenueChanged>(
            _ => Redraw(Overlay.Marquee | Overlay.QrCodes | Overlay.BreakMusicCard | Overlay.IdleCard | Overlay.Visualiser)));

        // Who is next is the marquee's content, however the queue, the turns or the mic moved it.
        _subscriptions.Add(broker.Subscribe<UpNextChanged>(_ => Redraw(Overlay.Marquee)));

        // An edit to the venue's playlist, or to a preset it names, applies to the song under way.
        _subscriptions.Add(broker.Subscribe<VisualisationPlaylistsChanged>(_ => Redraw(Overlay.Visualiser)));
        _subscriptions.Add(broker.Subscribe<VisualiserPresetsChanged>(_ => Redraw(Overlay.Visualiser)));

        // Who is at the mic decides whether a venue hides its codes and its marquee; what is on the
        // main channel decides the picture.
        _subscriptions.Add(broker.Subscribe<PlaybackChanged>(
            _ => Redraw(Overlay.QrCodes | Overlay.Picture | MarqueeIfPerformanceMoved())));

        // A provider moving to the next track says so apart from a start, pause or hand-off.
        _subscriptions.Add(broker.Subscribe<BreakMusicChanged>(_ => Redraw(Overlay.BreakMusicCard)));
        _subscriptions.Add(broker.Subscribe<BreakMusicTrackChanged>(_ => Redraw(Overlay.BreakMusicCard)));

        // Awaited rather than detached: the owner registering a code is waiting on this publish.
        _subscriptions.Add(broker.Subscribe<QrCodeOfferChanged>((_, _) => RedrawAsync(Overlay.QrCodes)));
        // How the words are adjusted moved, so the song on screen gets them again, mid-song.
        _subscriptions.Add(broker.Subscribe<TimedLyricsSettingsChanged>((_, _) => ReplaceTimedLyricsAsync()));
        _subscriptions.Add(broker.Subscribe<NextSingerAnnounced>((announced, _) => ShowNextSingerAsync(announced.Card)));
    }

    /// <summary>What it is, not where it is. "This computer" read as a location a host might be
    /// choosing between, next to receivers that really are places in the room.</summary>
    public string Name => "Local Display";

    /// <summary>The screen's own report of where the song is, timestamped against its measured offset.</summary>
    /// <remarks>Raised on the hub thread, as the server raises it; a handler must not wait on anything.</remarks>
    public event EventHandler<DisplayPlaybackStatus>? PlaybackStatusChanged;

    /// <summary>The second channel's track played out on its own, so whoever filled it owes another.</summary>
    /// <remarks>Domain-only: the second channel has no timeline in the contract, and only the
    /// library's break music, which rides it through this provider, needs to hear this.</remarks>
    public event EventHandler? BackgroundTrackEnded;

    /// <summary>The song played out to its end on the screen, reported once, stamped at the end.</summary>
    /// <remarks>Domain-only, as <see cref="BackgroundTrackEnded"/> is: <see cref="DisplayPlaybackStatus"/>
    /// has no way to say it, and <c>PlaybackService</c> concludes the song on it as well as on its
    /// clock. An end against a stream other than the one last loaded is dropped here.</remarks>
    public event EventHandler<DisplayPlaybackStatus>? SongEnded;

    /// <summary>The screen is holding the song back before its start, raised in place of
    /// <see cref="PlaybackStatusChanged"/> for as long as the hold runs.</summary>
    /// <remarks>Domain-only, for the same reason: the hold is this host's own setting, and the
    /// playhead must sit at the song's zero through it rather than run on and jump back.</remarks>
    public event EventHandler<DisplayPlaybackStatus>? HoldingBeforeSong;

    // --- finding devices ---

    /// <summary>True only while a launch is in flight; a screen takes seconds to register.</summary>
    public bool IsDiscovering => _launching;

    /// <summary>Nothing to find on this machine: the host launches the screen and it registers back.</summary>
    public bool SearchesForDevices => false;

    public IReadOnlyList<DisplayDevice> Devices
    {
        get
        {
            var connected = ConnectedScreen();

            return
            [
                new DisplayDevice
                {
                    Id = connected?.ScreenId ?? LocalScreenId,
                    Name = Name,

                    // The row's second line, which is where "where" belongs.
                    Model = "This computer",
                    IsConnected = connected is not null,

                    SupportsAudio = true,
                    SupportsVideo = true,

                    // It owns its own mixer, so a stop rides down instead of cutting.
                    SupportsFade = true,
                },
            ];
        }
    }

    /// <summary>Opens a screen, rather than looking for one.</summary>
    public async Task StartDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        if (_launching || ConnectedScreen() is not null) return;

        var launcher = _launchers.FirstOrDefault(p => p.IsAvailable);
        if (launcher is null)
        {
            _logger.LogWarning("No screen provider is available to launch a screen");
            return;
        }

        _launching = true;
        Announce();

        try
        {
            await launcher.LaunchAsync(LocalScreenId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not launch a screen");
        }
        finally
        {
            _launching = false;
            Announce();
        }
    }

    /// <summary>Nothing is being swept, so there is nothing to stop.</summary>
    public Task StopDiscoveryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    // --- connection ---

    public string? ConnectedDeviceId => ConnectedScreen()?.ScreenId;

    /// <summary>One per registration, so a screen that drops and comes back is a new session that
    /// holds nothing, and the host hands it the song again.</summary>
    public Guid? SessionId => _connected?.Id;

    /// <summary>True once <see cref="NotifyDisconnectRequested"/> ran for the screen now gone, so
    /// <c>PlaybackService</c> can tell a deliberate hand-off (Turn Off, a switch, host shutdown)
    /// from the screen disappearing on its own. Not part of <see cref="IDisplayProvider"/>: a
    /// plugin display's own disconnect keeps logging as an unexpected loss.</summary>
    public bool DisconnectWasRequested => _disconnectRequested;

    /// <summary>Opens the screen if it is not already up. Refused while a different one is.</summary>
    /// <remarks>Launching only starts the process; the screen still has to register back over IPC,
    /// which takes seconds. Answering as soon as the launch call returns reported a launch that
    /// was about to succeed as a refusal, so this waits for the registration itself instead.</remarks>
    public async Task<bool> ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (ConnectedScreen() is { } already)
            return already.ScreenId == deviceId;

        // Set before the launch starts: OnScreenConnected can run on the hub thread before this
        // method gets back from awaiting the launch, and a waiter created after that moment would
        // sit unanswered.
        var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _registering = waiter;

        await StartDiscoveryAsync(cancellationToken);

        if (ConnectedScreen() is not null)
            return true;

        using var timeout = new CancellationTokenSource(_registrationTimeout);
        using var timeoutRegistration = timeout.Token.Register(() => waiter.TrySetResult(false));
        using var cancelRegistration = cancellationToken.Register(() => waiter.TrySetResult(false));

        return await waiter.Task;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        NotifyDisconnectRequested();

        foreach (var launcher in _launchers)
        {
            try { launcher.CloseSpawnedScreens(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not close the screen"); }
        }

        Announce();

        return Task.CompletedTask;
    }

    /// <summary>Marks the loss that follows as one the host asked for. Called from
    /// <see cref="DisconnectAsync"/> for a Turn Off or a switch, and directly by the host's
    /// shutdown path, which closes the screen process without going through it.</summary>
    public void NotifyDisconnectRequested() => _disconnectRequested = true;

    // --- transport ---

    /// <summary>Takes the stems and draws the words itself, so it wants neither mixed nor burned in.</summary>
    /// <remarks>Both engines behind a screen decode Vorbis, so it can take the stems whole and ride
    /// their levels rather than making the host re-encode to move one.</remarks>
    public RenderTarget DescribeTarget() => new() { MixesStems = true, BurnLyrics = false };

    /// <summary>Sent whole, so the stems ride along with it; the page mixes when there are any.</summary>
    public async Task LoadAsync(DisplayLoad load, CancellationToken cancellationToken = default)
    {
        // Ahead of the load, so the venue's card is down before the song's first frame.
        await DrawPictureAsync(PictureCause.ProgramMoved);
        _loadedStreamUrl = load.StreamUrl ?? string.Empty;
        await SendAsync(ToCommand(load, IsGraphicsOnly(_services?.GetService<IPlaybackService>()?.CurrentProgram)));

        // After the load and before play, which every caller sends after this returns: a screen
        // holds the words until the next load, and one given them mid-song would light every
        // syllable already sung at once.
        await SendTimedLyricsAsync();

        // After the words, which it is decided on.
        await SendVisualiserAsync(load);
    }

    public Task PlayAsync(CancellationToken cancellationToken = default) => SendAsync(new PlayCommand());
    public Task PauseAsync(CancellationToken cancellationToken = default) => SendAsync(new PauseCommand());
    public Task StopAsync(TimeSpan? fade = null, CancellationToken cancellationToken = default)
        => SendAsync(new StopCommand { FadeDuration = fade });

    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
        => SendAsync(new SeekCommand { Position = position });

    public Task SetVolumeAsync(float volume, CancellationToken cancellationToken = default)
        => SendAsync(new SetVolumeCommand { Volume = volume });

    /// <summary>The page rides the level itself; false only when the send did not land.</summary>
    public Task<bool> SetStemVolumeAsync(StemLevel level, CancellationToken cancellationToken = default)
        => SendAsync(new SetStemVolumeCommand { Role = level.Role, Voice = level.Voice, Volume = level.Volume });

    // --- the second audio channel ---

    public Task LoadBackgroundAsync(BackgroundLoad background, CancellationToken cancellationToken = default)
        => SendAsync(new LoadBackgroundCommand { StreamUrl = background.StreamUrl, AutoPlay = background.AutoPlay });

    public Task PlayBackgroundAsync(CancellationToken cancellationToken = default)
        => SendAsync(new PlayBackgroundCommand());

    public Task PauseBackgroundAsync(CancellationToken cancellationToken = default)
        => SendAsync(new PauseBackgroundCommand());

    public Task StopBackgroundAsync(TimeSpan? fade = null, CancellationToken cancellationToken = default)
        => SendAsync(new StopBackgroundCommand { FadeDuration = fade });

    // --- plumbing ---

    internal static LoadMediaCommand ToCommand(DisplayLoad load, bool isGraphicsOnly = false) => new()
    {
        StreamUrl = load.StreamUrl,
        StreamStartOffset = load.StartOffset,
        Tempo = load.Tempo,
        Stems = load.Stems,
        IsGraphicsOnly = isGraphicsOnly,
    };

    /// <summary>Whether the program loading is CD+G, loose or zipped, whose picture the screen scales unsmoothed.</summary>
    /// <remarks>Read off the program because the load carries only URLs; the program is set before
    /// a display is asked to load it.</remarks>
    internal static bool IsGraphicsOnly(PlaybackProgram? program)
        => program is PlaybackProgram.Playing { Media.FilePath: { } path } && MediaFormats.IsCompactDiscGraphics(path);

    /// <summary>The marquee as the screen draws it, whole, from the venue's settings and who is next.</summary>
    /// <remarks>Disabled with no venue selected, one that has the marquee off, or one that hides it
    /// while <paramref name="performanceUnderWay"/>. The singers are
    /// exactly what <see cref="IUpNextService"/> answers for the venue's count, so the band and
    /// anything else naming who is next cannot disagree.</remarks>
    internal static async Task<SetMarqueeCommand> BuildMarqueeAsync(
        Venue.VenueSettings? settings, IUpNextService upNext, bool performanceUnderWay = false)
    {
        if (settings is null || !settings.MarqueeEnabled)
            return new SetMarqueeCommand { Enabled = false };

        if (settings.MarqueeHideDuringSong && performanceUnderWay)
            return new SetMarqueeCommand { Enabled = false };

        var upcoming = await upNext.ReadAsync(settings.MarqueeSingerCount);
        var glyph = MarqueeEntrySegmenter.ResolveGlyph(settings.MarqueeDividerShape);

        var segments = new List<MarqueeSegment>();
        foreach (var (entry, index) in upcoming.Select((entry, index) => (entry, index)))
        {
            // Glyph null means "None" was chosen: entries run together with no divider at all.
            if (index > 0 && glyph is not null)
                segments.Add(new MarqueeSegment { Text = "", Kind = MarqueeSegmentKind.Separator });

            segments.AddRange(MarqueeEntrySegmenter.ComposeSegments(entry, settings.MarqueeEntryFormat));
        }

        return new SetMarqueeCommand
        {
            Enabled = true,
            Entries = segments,
            Message = MarqueeText.CollapseToOneLine(settings.MarqueeMessage),
            Position = settings.MarqueePosition,

            // A cleared colour is no colour, not an empty CSS value the screen would take.
            BackgroundColor = string.IsNullOrWhiteSpace(settings.MarqueeBackgroundColor) ? null : settings.MarqueeBackgroundColor.Trim(),
            TextColor = string.IsNullOrWhiteSpace(settings.MarqueeTextColor) ? null : settings.MarqueeTextColor.Trim(),
            SingerColor = string.IsNullOrWhiteSpace(settings.MarqueeSingerColor) ? null : settings.MarqueeSingerColor.Trim(),
            SongColor = string.IsNullOrWhiteSpace(settings.MarqueeSongColor) ? null : settings.MarqueeSongColor.Trim(),
            DividerColor = string.IsNullOrWhiteSpace(settings.MarqueeDividerColor) ? null : settings.MarqueeDividerColor.Trim(),
            DividerGlyph = glyph,
            BackgroundOpacityPercent = settings.MarqueeBackgroundOpacity,
            FontSizePixels = settings.MarqueeFontSizePixels,
            ScrollSpeed = settings.MarqueeScrollSpeed,
            PinLabel = settings.MarqueePinLabel,
        };
    }

    /// <summary>A failed send never costs the song: a screen that has gone is not an error here.</summary>
    private async Task<bool> SendAsync(IScreenCommand command)
    {
        try
        {
            await _screenServer.BroadcastCommandAsync(command);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send {Command} to the screen", command.GetType().Name);
            return false;
        }
    }

    /// <summary>Sends an overlay unless this session's screen already shows exactly that.</summary>
    /// <remarks>Compared as sent, not by reference or record equality: a rebuilt command is a new
    /// object, and the codes carry a list, which a record compares by reference.</remarks>
    private async Task SendOverlayAsync(IScreenCommand command)
    {
        var type = command.GetType();
        var drawn = JsonSerializer.Serialize(command, type);
        var session = _connected?.Id;

        lock (_overlaysSent)
        {
            if (_overlaysSentOn != session)
            {
                _overlaysSent.Clear();
                _overlaysSentOn = session;
            }

            // With no screen up there is nothing it could already be showing.
            if (session is not null && _overlaysSent.TryGetValue(type, out var last) && last == drawn)
                return;

            _overlaysSent[type] = drawn;
        }

        if (await SendAsync(command)) return;

        // Forgotten, so the next redraw tries again rather than trusting a send that never landed.
        lock (_overlaysSent)
        {
            if (_overlaysSentOn == session && _overlaysSent.TryGetValue(type, out var recorded) && recorded == drawn)
                _overlaysSent.Remove(type);
        }
    }

    /// <summary>A field read, deliberately: see <see cref="_connected"/>.</summary>
    private IScreenConnection? ConnectedScreen() => _connected?.Connection;

    /// <summary>Gives the screen the words for the song now loading, or clears the last song's.</summary>
    /// <remarks>Read once per song and resent only to a screen that has not had them: a rebuild at a
    /// new key reloads the same program onto a screen still holding them. An ad has no words and
    /// sends none. Never throws: a song whose timing cannot be read plays like one that has none,
    /// and a song with none still sends, or the screen keeps lighting the last song's.</remarks>
    private async Task SendTimedLyricsAsync()
    {
        if (_services?.GetService<IPlaybackService>()?.CurrentProgram is not PlaybackProgram.Playing { Performance: not null } song)
            return;

        await _lyricsLock.WaitAsync();
        try
        {
            var session = _connected?.Id;

            if (!ReferenceEquals(song, _lyricsFor))
            {
                _lyricsFor = song;
                _lyrics = await ReadTimedLyricsAsync(song.Media);
            }
            else if (session == _lyricsSentOn)
            {
                return;
            }

            _lyricsSentOn = session;

            await SendAsync(new SetTimedLyricsCommand
            {
                Lyrics = _lyrics,
                Intro = BuildIntroCard(song.Media, _lyrics),
                LeadInSeconds = LeadInGrace.PreRollSeconds(_lyrics, LeadInGraceSeconds()),
            });
        }
        finally
        {
            _lyricsLock.Release();
        }
    }

    /// <summary>Reads the loaded song's words again and hands them to a screen already holding the
    /// last read, which swaps them in without restarting anything.</summary>
    /// <remarks>Only for a song that had words: the adjustments work on words, so a song with none
    /// still has none. A screen that has not had this song's words yet gets them whole on its own
    /// load, which reads through the same service.</remarks>
    private async Task ReplaceTimedLyricsAsync()
    {
        if (_services?.GetService<IPlaybackService>()?.CurrentProgram is not PlaybackProgram.Playing { Performance: not null } song)
            return;

        await _lyricsLock.WaitAsync();
        try
        {
            // A screen that has not had this song's words yet is sent them whole by its replay.
            var session = _connected?.Id;
            if (!ReferenceEquals(song, _lyricsFor) || session is null || session != _lyricsSentOn) return;

            if (await ReadTimedLyricsAsync(song.Media) is not { } lyrics) return;

            _lyrics = lyrics;

            await SendAsync(new SetTimedLyricsCommand
            {
                Lyrics = lyrics,
                Intro = BuildIntroCard(song.Media, lyrics),
                LeadInSeconds = LeadInGrace.PreRollSeconds(lyrics, LeadInGraceSeconds()),
                Replacing = true,
            });
        }
        finally
        {
            _lyricsLock.Release();
        }
    }

    /// <summary>The song and its singer, shown until the words start; only for words drawn here.</summary>
    /// <remarks>Decided by the timing alone, never the format: a song whose picture carries its own
    /// words carries its own intro, and sends no timing. A timing with no pages has no first page
    /// for the card to give way to, and a song with no title has nothing to head it.</remarks>
    private ScreenIntroCard? BuildIntroCard(Media media, TimedLyrics? lyrics)
    {
        if (lyrics is not { Pages.Count: > 0 } || string.IsNullOrWhiteSpace(media.Title))
            return null;

        // The name playback settled at load, alias rule applied, so the card and the console agree.
        var singer = _services?.GetService<IPlaybackService>()?.CurrentSingerName;

        return new ScreenIntroCard
        {
            Title = media.Title.Trim(),
            Artist = string.IsNullOrWhiteSpace(media.Artist) ? null : media.Artist.Trim(),
            Singer = string.IsNullOrWhiteSpace(singer) ? null : singer.Trim(),
        };
    }

    private static SetVisualiserCommand VisualiserOff => new() { Enabled = false };

    /// <summary>Tells the screen whether to draw a visualiser under the playing song, and which.</summary>
    /// <remarks>On a load, decided for that load; with none (a venue edit), re-decided for the song
    /// already loaded, and skipped for a song whose own load has not decided it yet. Never throws:
    /// the visualiser is decoration, and a song plays over black without it.</remarks>
    private async Task SendVisualiserAsync(DisplayLoad? load)
    {
        if (_services?.GetService<IPlaybackService>()?.CurrentProgram is not PlaybackProgram.Playing song)
            return;

        await _visualiserLock.WaitAsync();
        try
        {
            if (!ReferenceEquals(song, _visualiserFor))
            {
                if (load is null) return;

                _visualiserFor = song;
                _visualiserPick = null;
                _visualiserSourceHasPicture = null;
                _visualiserLevelsUrl = null;
                _visualiserVideo = null;
                _visualiserVideoPending = null;
            }

            if (load is not null) _visualiserLoad = load;
            if (_visualiserLoad is not { } loaded) return;

            await SendAsync(await DecideVisualiserAsync(song, loaded));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not decide the visualiser for '{Title}'", song.Media.Title);
        }
        finally
        {
            _visualiserLock.Release();
        }
    }

    /// <summary>On only for a performance with timed words and nothing of its own to show under them
    /// (<see cref="SongBackdrops.ForPlaying"/> answering black): the performance's own background
    /// when it names one, else an entry from the venue's visualisation playlist.</summary>
    /// <remarks>Any preset the timing names is ignored. A background that is black draws nothing; a
    /// look that does not resolve draws the venue's playlist. An ad reads no words
    /// (<see cref="SendTimedLyricsAsync"/>), so it never gets one.</remarks>
    private async Task<SetVisualiserCommand> DecideVisualiserAsync(PlaybackProgram.Playing song, DisplayLoad load)
    {
        var playlistId = (await ReadVenueSettingsAsync())?.VisualisationPlaylistId;
        var background = await ReadBackgroundAsync(song);

        // Ahead of the probe below: a venue with no playlist and a song with no look of its own
        // draws nothing, and must not cost a read of the file.
        if (playlistId is null && background is not { Type: PerformanceBackgroundType.Look }) return VisualiserOff;

        var lyrics = ReferenceEquals(song, _lyricsFor) ? _lyrics : null;
        if (lyrics is not { Pages.Count: > 0 }) return VisualiserOff;

        var path = song.Media.FilePath;
        var backdrop = SongBackdrops.ForPlaying(hasTimedLyrics: true, path, await PlaysOwnPictureAsync(path, load));
        if (backdrop != SongBackdrop.Black) return VisualiserOff;

        if (background is { Type: PerformanceBackgroundType.Black }) return VisualiserOff;

        if (background is { Type: PerformanceBackgroundType.Look }
            && LookForSong(song, background, () => LevelsUrlFor(path, load)) is { } own)
            return own;

        if (playlistId is not { } venuePlaylistId) return VisualiserOff;
        if (_services?.GetService<IVisualisationPlaylistService>() is not { } playlists) return VisualiserOff;

        // Picked only once the song is known to draw one, so a video does not use up a turn.
        if (await EntryForSongAsync(playlists, venuePlaylistId) is not { } entry) return VisualiserOff;

        // Never through the levels: a video is drawn muted and does not follow the song.
        if (entry.PresetSource == VisualiserPresetSource.Video) return VideoVisualiserFor(song, entry, entry.Id) ?? VisualiserOff;

        return VisualiserFor(entry, song.Media.Title, () => LevelsUrlFor(path, load)) ?? VisualiserOff;
    }

    /// <summary>The performance's own background, read afresh: the program carries the row as it was
    /// at load. Null when it names none, or it cannot be read.</summary>
    private async Task<PerformanceBackground?> ReadBackgroundAsync(PlaybackProgram.Playing song)
    {
        if (song.Performance is not { } performance || _services?.GetService<IPerformanceService>() is not { } performances)
            return null;

        return (await performances.ReadAsync(performance.Id))?.Background;
    }

    /// <summary>The command that draws the performance's own look; null when it does not resolve,
    /// so the venue's playlist draws instead. A video whose URL is still being found is drawn as
    /// off meanwhile, not handed to the playlist.</summary>
    private SetVisualiserCommand? LookForSong(PlaybackProgram.Playing song, PerformanceBackground look, Func<string?> levelsUrl)
    {
        if (look.PresetSource != VisualiserPresetSource.Video) return VisualiserFor(look, song.Media.Title, levelsUrl);

        // Known not to play, or naming none: the playlist answers, and keeps its own pick.
        if (look.VideoMediaId is not { } mediaId || _visualiserVideo is { Url: null } known && known.MediaId == mediaId)
            return null;

        var pick = LookPick(song);
        _visualiserPick = pick;
        return VideoVisualiserFor(song, look, pick.EntryId) ?? VisualiserOff;
    }

    /// <summary>The pick a performance's own look is kept under: no playlist has an empty id, so it
    /// never matches a playlist's entry, and a late video URL for it is still this song's.</summary>
    private static (Guid PlaylistId, Guid EntryId) LookPick(PlaybackProgram.Playing song)
        => (Guid.Empty, song.Performance!.Id);

    /// <summary>The command that draws <paramref name="entry"/>; null when its preset cannot be
    /// found, which leaves whatever was under it.</summary>
    /// <remarks><paramref name="levelsUrl"/> is asked only once the preset resolves: it may start a
    /// read of the song's levels.</remarks>
    private SetVisualiserCommand? VisualiserFor(IVisualisationLook entry, string drawnFor, Func<string?> levelsUrl)
    {
        string? name = null, url = null, builtIn = null;
        if (entry.PresetSource == VisualiserPresetSource.BuiltIn)
        {
            if (!VisualiserPresetService.BuiltIns.Any(b => b.Name == entry.PresetName))
            {
                _logger.LogWarning("The visualisation names a built-in drawing '{Preset}' the host does not have; '{Title}' draws without it",
                    entry.PresetName, drawnFor);
                return null;
            }

            builtIn = entry.PresetName;
        }
        else if (entry.PresetSource == VisualiserPresetSource.Imported)
        {
            if (ImportedPresetUrl(entry.PresetName) is not { } imported)
            {
                _logger.LogWarning("The visualisation's imported preset '{Preset}' is not there any more; '{Title}' draws without it",
                    entry.PresetName, drawnFor);
                return null;
            }

            url = imported;
        }
        else
        {
            name = entry.PresetName;
        }

        return new SetVisualiserCommand
        {
            Enabled = true,
            PresetName = name,
            PresetUrl = url,
            BuiltIn = builtIn,
            BarCount = entry.BarCount,
            ColourScheme = entry.ColourScheme,
            Colour = entry.Colour,
            Brightness = entry.Brightness,
            Saturation = entry.Saturation,
            Sensitivity = entry.Sensitivity,
            LevelsUrl = levelsUrl(),
        };
    }

    /// <summary>The command that plays <paramref name="entry"/>'s video, once its URL is known; null
    /// until then, or when it cannot be had.</summary>
    /// <remarks>The first ask for a song is answered off the load path: an encode can take seconds,
    /// and the song must not wait on its decoration. <see cref="ResolveVideoAsync"/> decides again
    /// once it is known.</remarks>
    private SetVisualiserCommand? VideoVisualiserFor(PlaybackProgram.Playing song, IVisualisationLook entry, Guid pickId)
    {
        if (entry.VideoMediaId is not { } mediaId)
        {
            _logger.LogWarning("The visualisation names no video; '{Title}' draws without it", song.Media.Title);
            return null;
        }

        if (_visualiserVideo is { } known && known.MediaId == mediaId)
        {
            if (known.Url is null) return null;

            return new SetVisualiserCommand
            {
                Enabled = true,
                VideoUrl = known.Url,
                Brightness = entry.Brightness,
                Saturation = entry.Saturation,
            };
        }

        if (_visualiserVideoPending != mediaId)
        {
            _visualiserVideoPending = mediaId;
            _ = Task.Run(() => ResolveVideoAsync(song, pickId, mediaId));
        }

        return null;
    }

    /// <summary>Finds where the screen plays a video entry's video, then draws it if the same song
    /// and the same entry are still current.</summary>
    /// <remarks>Never throws. A song that ended, or an entry replaced, while it was encoding gets
    /// nothing.</remarks>
    private async Task ResolveVideoAsync(PlaybackProgram.Playing song, Guid entryId, Guid mediaId)
    {
        string? url = null;
        try
        {
            var media = _services?.GetService<IMediaService>() is { } library ? await library.ReadAsync(mediaId) : null;

            if (media is null)
                _logger.LogWarning("The visualisation's video {MediaId} is not in the library; '{Title}' draws without it", mediaId, song.Media.Title);
            else if (media.Type != MediaType.Video)
                _logger.LogWarning("The visualisation names '{Video}', which is not a video; '{Title}' draws without it", media.Title, song.Media.Title);
            else if (_services?.GetService<IVideoBackdropService>() is { } backdrops)
                url = await backdrops.UrlForAsync(media);

            if (media is { Type: MediaType.Video } && url is null)
                _logger.LogWarning("The screen cannot play '{Video}'; '{Title}' draws without it", media.Title, song.Media.Title);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not find the visualisation's video {MediaId} for '{Title}'", mediaId, song.Media.Title);
        }

        await _visualiserLock.WaitAsync();
        try
        {
            if (!ReferenceEquals(song, _visualiserFor)
                || !ReferenceEquals(song, _services?.GetService<IPlaybackService>()?.CurrentProgram))
                return;

            // Kept even for an entry since replaced: it is this song's answer for this video.
            if (_visualiserVideoPending == mediaId) _visualiserVideoPending = null;
            _visualiserVideo = (mediaId, url);

            if (_visualiserPick?.EntryId != entryId || _visualiserLoad is not { } loaded) return;

            // A playlist's video that cannot play stays black; a performance's own falls back to the playlist.
            if (url is null && _visualiserPick?.PlaylistId != Guid.Empty) return;

            // Decided whole again rather than sent as found: the venue or the entry may have moved.
            await SendAsync(await DecideVisualiserAsync(song, loaded));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not draw the visualisation's video for '{Title}'", song.Media.Title);
        }
        finally
        {
            _visualiserLock.Release();
        }
    }

    /// <summary>Hands the screen the "Up next" card with what the venue wants behind it.</summary>
    /// <remarks>A one-shot: a venue edit while it is up reaches the next announcement, not this one.
    /// A visualisation the venue cannot draw (no playlist, an empty one, a preset gone) falls back
    /// to <see cref="NextSingerBackground.Over"/>, what a venue that never chose gets.</remarks>
    private async Task ShowNextSingerAsync(NextSingerCard card)
    {
        var background = (await ReadVenueSettingsAsync())?.NextSingerBackground ?? NextSingerBackground.Over;
        SetVisualiserCommand? visualiser = null;

        if (background == NextSingerBackground.Visualisation)
        {
            visualiser = await CardVisualiserAsync(card);
            if (visualiser is null) background = NextSingerBackground.Over;
        }

        await SendAsync(new ShowNextSingerCommand
        {
            Singer = card.Singer,
            Song = card.Song,
            Artist = card.Artist,
            Background = background,
            Visualiser = visualiser,
        });
    }

    /// <summary>The venue playlist's next entry, taken the way a song takes one, with no levels:
    /// between singers there is no song for the host to read them from.</summary>
    /// <remarks>Never throws: the card matters more than what is behind it.</remarks>
    private async Task<SetVisualiserCommand?> CardVisualiserAsync(NextSingerCard card)
    {
        try
        {
            if ((await ReadVenueSettingsAsync())?.VisualisationPlaylistId is not { } playlistId) return null;
            if (_services?.GetService<IVisualisationPlaylistService>() is not { } playlists) return null;

            // An empty or missing playlist answers null.
            if (await playlists.SelectNextAsync(playlistId) is not { } entry) return null;

            // The turn is taken all the same: looking on for a drawing would spend two.
            if (entry.PresetSource == VisualiserPresetSource.Video) return null;

            return VisualiserFor(entry, "the next-singer card", () => null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not pick a visualisation for the card naming {Singer}", card.Singer);
            return null;
        }
    }

    /// <summary>The song's entry: the one already picked while it is still in the venue's playlist,
    /// read afresh so an edit to its settings shows mid-song; else the playlist's next.</summary>
    private async Task<VisualisationEntry?> EntryForSongAsync(IVisualisationPlaylistService playlists, Guid playlistId)
    {
        // An empty or missing playlist falls through to SelectNextAsync, which answers null.
        var playlist = await playlists.ReadWithEntriesAsync(playlistId);

        if (_visualiserPick is { } pick && pick.PlaylistId == playlistId
            && playlist?.Entries.FirstOrDefault(entry => entry.Id == pick.EntryId) is { } kept)
            return kept;

        var next = await playlists.SelectNextAsync(playlistId);
        _visualiserPick = next is null ? null : (playlistId, next.Id);

        return next;
    }

    /// <summary>Where the screen fetches an imported preset, versioned by when it was written so a
    /// re-import reaches a screen already drawing it; null when no such preset is stored.</summary>
    private string? ImportedPresetUrl(string name)
    {
        if (_services?.GetService<IVisualiserPresetService>() is not { } presets) return null;
        if (_services.GetService<IOptionsMonitor<HlsMediaStreamService.ServiceOptions>>() is not { } options) return null;

        var preset = presets.ReadAll().FirstOrDefault(p => p.Source == VisualiserPresetSource.Imported && p.Name == name);
        if (preset is null) return null;

        return $"{options.CurrentValue.BaseAddress.TrimEnd('/')}{VisualiserPresetService.RoutePrefix}"
            + $"{Uri.EscapeDataString(name)}?v={preset.ImportedUtc?.Ticks ?? 0}";
    }

    /// <summary>Starts reading the song's levels the first time its visualiser is on, and answers
    /// where they are served; null when there is nothing the host can read them from.</summary>
    /// <remarks>Read for every song that draws one, whether or not the screen can listen for itself:
    /// the screen decides which it uses, and a stem song that is later re-keyed becomes an encoded
    /// one the screen cannot hear. A load with nothing readable is asked again on the next load.</remarks>
    private string? LevelsUrlFor(string path, DisplayLoad load)
    {
        if (_visualiserLevelsUrl is not null) return _visualiserLevelsUrl;
        if (_services?.GetService<ISongLevelsService>() is not { } levels) return null;

        var stems = _services.GetService<IStemStreamService>();

        // A stem song loaded already re-keyed arrives as the host's own mix of its stems.
        var mixed = load.Stems.Count == 0 && load.StreamUrl is { } stream ? stems?.StemsMixedInto(stream) : null;

        var inputs = SongLevels.InputsFor(path, load, url =>
        {
            try { return stems?.ResolveStemInput(url); }
            catch (InvalidOperationException) { return null; }
        }, mixed);

        if (inputs is null) return null;

        return _visualiserLevelsUrl = levels.Begin(inputs);
    }

    /// <summary>Takes the visualiser down between songs, and drops the last song's levels with it.</summary>
    private async Task TakeDownVisualiserAsync()
    {
        _services?.GetService<ISongLevelsService>()?.Clear();
        await SendAsync(VisualiserOff);
    }

    /// <summary>Whether the screen shows a picture from the song's own file for this load.</summary>
    /// <remarks>Stems are sound only. With no probe to ask, the answer is yes: a visualiser drawn
    /// over a singer's own video is worse than black under words.</remarks>
    private async Task<bool> PlaysOwnPictureAsync(string path, DisplayLoad load)
    {
        if (load.Stems.Count > 0 || load.StreamUrl is null) return false;

        // Free, and nothing from an audio file is ever shown, so the probe is skipped.
        if (!SongBackdrops.MayShowPictureFrom(path)) return false;

        if (_visualiserSourceHasPicture is { } known) return known;

        if (_services?.GetService<ISourcePictureProbe>() is not { } probe) return true;

        var hasPicture = await probe.HasMovingPictureAsync(path);
        _visualiserSourceHasPicture = hasPicture;

        return hasPicture;
    }

    /// <summary>The machine's grace, read on every send so an App Settings change needs no restart.</summary>
    private int LeadInGraceSeconds()
        => _playbackOptions?.CurrentValue.LeadInGraceSeconds ?? 0;

    private async Task<TimedLyrics?> ReadTimedLyricsAsync(Media media)
    {
        if (_services?.GetService<ITimedLyricsService>() is not { } lyrics) return null;

        try { return await lyrics.GetTimedLyricsAsync(media.FilePath); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the lyric timing for '{Title}'", media.Title);
            return null;
        }
    }

    /// <summary>Puts both channels at full level; the room's mixer sets the real one.</summary>
    /// <remarks>Sent on every connect so a screen never keeps a level from an earlier run.</remarks>
    private async Task ApplyVolumeAsync()
    {
        if (ConnectedScreen() is null) return;

        await SendAsync(new SetVolumeCommand { Volume = FullVolume });
        await SendAsync(new SetBackgroundVolumeCommand { Volume = FullVolume });
    }

    /// <summary>Pulls the current state of each overlay asked for and sends it whole.</summary>
    /// <remarks>Never throws: one overlay that cannot be built must not keep the rest off the screen.</remarks>
    private async Task RedrawAsync(Overlay overlays)
    {
        if (overlays.HasFlag(Overlay.Volume))
        {
            try { await ApplyVolumeAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not set the screen's volume"); }
        }

        // Ahead of the overlays, which read the database: the picture is what the room notices late.
        if (overlays.HasFlag(Overlay.Connected))
            await DrawPictureAsync(PictureCause.Connected);
        else if (overlays.HasFlag(Overlay.Picture))
            await DrawPictureAsync(PictureCause.ProgramMoved);
        else if (overlays.HasFlag(Overlay.IdleCard))
            await DrawPictureAsync(PictureCause.VenueChanged);

        if (overlays.HasFlag(Overlay.Visualiser))
            await SendVisualiserAsync(load: null);

        if (overlays.HasFlag(Overlay.Marquee))
            await DrawAsync<IUpNextService>("marquee", async upNext =>
                await BuildMarqueeAsync(await ReadVenueSettingsAsync(), upNext, PerformanceUnderWay()));

        // Sent even when there is nothing up: it is the whole state, so it also clears a code left
        // on a screen that dropped and came back.
        if (overlays.HasFlag(Overlay.QrCodes))
            await DrawAsync<IQrCodeOfferService>("QR codes", async codes => BuildQrCodes(await codes.ReadOfferAsync()));

        if (overlays.HasFlag(Overlay.BreakMusicCard))
            await DrawAsync<IBreakMusicService>("break music card", BuildBreakMusicCardAsync);
    }

    /// <summary>The offer as the screen draws it, or an empty set that clears whatever is up.</summary>
    private SetScreenQrCodesCommand BuildQrCodes(QrCodeOffer? offer)
    {
        if (offer is null)
            return new SetScreenQrCodesCommand();

        var (image, modules) = Encode(offer.Payload);

        return new SetScreenQrCodesCommand
        {
            Codes =
            [
                new ScreenQrCodePlacement
                {
                    ImageUrl = image,
                    Modules = modules,
                    Caption = offer.Caption,
                    Corner = offer.Corner ?? OverlayCorner.BottomRight,
                    Size = offer.Size ?? QrCodeSize.Medium,

                    // Resolved here, not on the screen, which decides nothing.
                    SafeZone = offer.SafeZone ?? DefaultSafeZone,
                    Offset = offer.Offset ?? DefaultOffset,
                },
            ],
        };
    }

    /// <summary>Encodes the payload as an SVG at the lowest correction level, fewer modules.</summary>
    private (string Image, int Modules) Encode(string payload)
    {
        lock (_encoded)
        {
            if (_encoded.TryGetValue(payload, out var cached))
                return cached;
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.L);

        // One unit per module, so every size the screen asks for is an exact multiple of the grid.
        // No quiet zone drawn in: the screen paints that margin, so a corner can be as tight as it likes.
        var svg = new SvgQRCode(data).GetGraphic(1, "#000000", "#ffffff", drawQuietZones: false);

        // ModuleMatrix counts the quiet zone whether or not it is drawn, so the eight rows and
        // columns of it come off: what the screen sizes against has to be what is in the picture.
        var encoded = (
            $"data:image/svg+xml;base64,{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg))}",
            data.ModuleMatrix.Count - 8);

        lock (_encoded)
        {
            _encoded[payload] = encoded;
        }

        return encoded;
    }

    /// <summary>Someone is at the mic, playing or paused; an ad or an idle screen is not a song.</summary>
    private bool PerformanceUnderWay()
        => _services?.GetService<IPlaybackService>()?.CurrentPerformance is not null;

    /// <summary>The marquee only when a performance started or ended since the last PlaybackChanged.</summary>
    /// <remarks>PlaybackChanged is also every pause and seek, and the marquee reads the queue, so it
    /// is not rebuilt on those. Broker handlers run one at a time, so the field needs no lock.</remarks>
    private Overlay MarqueeIfPerformanceMoved()
    {
        var underWay = PerformanceUnderWay();
        if (underWay == _performanceUnderWay) return default;

        _performanceUnderWay = underWay;
        return Overlay.Marquee;
    }

    private async Task<Venue.VenueSettings?> ReadVenueSettingsAsync()
        => _venuesService is null ? null : (await _venuesService.ReadSelectedVenueAsync())?.Settings;

    /// <summary>Names the break music in a corner; says what is playing, not what is cued.</summary>
    private async Task<IScreenCommand> BuildBreakMusicCardAsync(IBreakMusicService breakMusic)
    {
        var settings = await ReadVenueSettingsAsync();

        // A venue that wants none gets none, and a console with no venue selected has nobody to
        // have asked, which is the same answer.
        if (settings is null || !settings.BreakMusicCardEnabled)
            return new SetBreakMusicCardCommand { Enabled = false };

        // Playing only: Paused and Suspended both mean the room is hearing something else, and a
        // card naming a track nobody can hear is worse than no card.
        if (breakMusic.State != BreakMusicState.Playing || breakMusic.CurrentTrack is not { } track)
            return new SetBreakMusicCardCommand { Enabled = false };

        // A provider that reports no title has nothing worth a corner of the picture.
        if (string.IsNullOrWhiteSpace(track.Title))
            return new SetBreakMusicCardCommand { Enabled = false };

        return new SetBreakMusicCardCommand
        {
            Enabled = true,
            Title = track.Title,

            // Null rather than blank where the provider could not say, so the screen draws one
            // line instead of a gap it has to reason about.
            Artist = string.IsNullOrWhiteSpace(track.Artist) ? null : track.Artist,
            Corner = settings.BreakMusicCardCorner ?? DefaultBreakMusicCardCorner,

            // The codes' setting, because the inset belongs to the corner.
            Offset = settings.QrCodeOffset > 0 ? settings.QrCodeOffset : DefaultOffset,
        };
    }

    /// <summary>Puts up the venue's card, an ad's still, or takes either down for a song.</summary>
    /// <remarks>Drawn only when the program has moved, since PlaybackChanged also means a seek or a
    /// pause. A screen that has just connected gets the picture whatever it was last sent, and a
    /// venue edit redraws only the card: a still over a singer is worse than a stale one.</remarks>
    private async Task DrawPictureAsync(PictureCause cause)
    {
        if (_services?.GetService<IPlaybackService>() is not { } playback) return;

        await _pictureLock.WaitAsync();
        try
        {
            var program = playback.CurrentProgram;

            switch (cause)
            {
                case PictureCause.ProgramMoved when Equals(program, _pictured):
                case PictureCause.VenueChanged when program is not PlaybackProgram.Idle:
                    return;
            }

            // A joiner shows nothing until the song reloads onto it, so there is nothing to take down.
            // Left unrecorded: a load racing the connect would find the song pictured and keep the card up.
            if (cause == PictureCause.Connected && program is PlaybackProgram.Playing) return;

            _pictured = program;

            switch (program)
            {
                case PlaybackProgram.AdStill still:
                    await TakeDownVisualiserAsync();
                    await SendAsync(new ShowImageCommand { Url = still.ImageUrl, Scaling = still.Scaling });
                    break;

                case PlaybackProgram.Playing:
                    await SendAsync(new HideImageCommand());
                    break;

                default:
                    // Between songs is the venue's card, never the last song's visualiser.
                    await TakeDownVisualiserAsync();
                    await DrawIdleCardAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            // Decoration: failing to draw it must not stop the queue moving on to the next singer.
            _logger.LogWarning(ex, "Could not draw the screen's picture");
        }
        finally
        {
            _pictureLock.Release();
        }
    }

    /// <summary>The venue's card while nothing is playing, or a clear screen when it has none.</summary>
    private async Task DrawIdleCardAsync()
    {
        var venue = _venuesService is null ? null : await _venuesService.ReadSelectedVenueAsync();

        if (venue?.Settings.BrandingImageMediaId is not { } brandingId
            || _services?.GetService<IMediaService>() is not { } library
            || _services.GetService<IMediaStreamService>() is not { } streams)
        {
            await SendAsync(new HideImageCommand());
            return;
        }

        // A branding row pointing at a song would be handed over as an image URL serving nothing.
        if (await library.ReadAsync(brandingId) is not { } media || !MediaFormats.IsImage(media.Format))
        {
            await SendAsync(new HideImageCommand());
            return;
        }

        // The venue's scaling wins when set: the same picture can be the card in two rooms of
        // different shapes, and a host adjusting it here need not go edit the library image.
        await SendAsync(new ShowImageCommand
        {
            Url = streams.BuildImageUrl(media.Id),
            Scaling = venue.Settings.BrandingImageScaling ?? media.ImageScaling,
        });
    }

    private async Task DrawAsync<TSource>(string what, Func<TSource, Task<IScreenCommand>> build)
        where TSource : class
    {
        if (_services?.GetService<TSource>() is not { } source) return;

        try { await SendOverlayAsync(await build(source)); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not build the {Overlay} for the screen", what); }
    }

    /// <summary>Detached, because broker handlers run one at a time and a redraw reads the database.</summary>
    /// <remarks>Owed overlays pile up for <see cref="_redrawSettle"/> and are drawn once, one
    /// redraw at a time, so a burst of announcements from one host action lands as one draw of
    /// where it ended rather than one per step, some of them mid-way.</remarks>
    private void Redraw(Overlay overlays)
    {
        lock (_redrawGate)
        {
            _redrawOwed |= overlays;

            if (_redrawRunning) return;
            _redrawRunning = true;
        }

        _ = Task.Run(DrainRedrawsAsync);
    }

    private async Task DrainRedrawsAsync()
    {
        while (true)
        {
            // Waited out before every draw, including one owed while the last was drawing: the
            // window has to start at a burst's first announcement or it splits the burst.
            await Task.Delay(_redrawSettle);

            Overlay overlays;

            lock (_redrawGate)
            {
                overlays = _redrawOwed;
                _redrawOwed = 0;
            }

            try { await RedrawAsync(overlays); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not redraw the screen"); }

            lock (_redrawGate)
            {
                if (_redrawOwed == 0)
                {
                    _redrawRunning = false;
                    return;
                }
            }
        }
    }

    // Raised on the hub thread with a plain EventHandler, so everything here that awaits is
    // detached: awaiting on it would be async void.
    private void OnScreenConnected(object? sender, ScreenConnectionEventArgs e)
    {
        _connected = new ScreenSession(e.Connection, Guid.NewGuid());
        _disconnectRequested = false;

        // Answers a ConnectAsync waiting on this launch; a screen that registers without anyone
        // waiting (a relaunch, or one recovering on its own) leaves this null and the call no-ops.
        _registering?.TrySetResult(true);

        Announce();

        // The whole current state, pulled fresh: a screen joining mid-show has been sent none of
        // it, and one that dropped and came back may still be drawing what has since changed.
        Redraw(Overlay.All | Overlay.Connected);
    }

    /// <summary>Hands the screen's reports on as a display's: the song's clock, or the bed ending.</summary>
    private void OnStateReceived(object? sender, ScreenStateReceivedEventArgs e)
    {
        switch (e.State)
        {
            // Unsampled means the screen has no measured offset yet, and an unanchored report would
            // move the playhead by however long it spent in flight.
            case ScreenPlaybackState { SampledAtUtc: { } sampledAt } state:
                var status = new DisplayPlaybackStatus
                {
                    Position = state.Position,
                    IsPlaying = state.IsPlaying,
                    SampledAtUtc = sampledAt,
                };

                if (state.HasEnded)
                {
                    if ((state.StreamUrl ?? string.Empty) == _loadedStreamUrl)
                        SongEnded?.Invoke(this, status);
                    else
                        _logger.LogInformation("Dropped an end reported for {Stream}, which is no longer loaded", state.StreamUrl);
                }
                else if (state.IsHolding)
                    HoldingBeforeSong?.Invoke(this, status);
                else
                    PlaybackStatusChanged?.Invoke(this, status);
                break;

            case ScreenBackgroundState { HasEnded: true }:
                BackgroundTrackEnded?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    /// <summary>Matched on the connection, not the screen id: a screen coming back under the same
    /// id is already tracked by the time its old connection's disconnect arrives, and clearing on
    /// the id would take the live one down with the stale one.</summary>
    private void OnScreenDisconnected(object? sender, ScreenConnectionEventArgs e)
    {
        if (_connected?.Connection.ConnectionId == e.Connection.ConnectionId)
            _connected = null;

        Announce();
    }

    private void Announce() => _ = Task.Run(() => _broker.PublishAsync(new DisplaysChanged()));

    public void Dispose()
    {
        _screenServer.ScreenConnected -= OnScreenConnected;
        _screenServer.ScreenDisconnected -= OnScreenDisconnected;
        _screenServer.StateReceived -= OnStateReceived;
        _subscriptions.Dispose();
    }

    /// <summary>One registration of the screen; a screen that comes back is a new one.</summary>
    private sealed record ScreenSession(IScreenConnection Connection, Guid Id);

    [Flags]
    private enum Overlay
    {
        Volume = 1,
        Marquee = 2,
        QrCodes = 4,
        BreakMusicCard = 8,

        /// <summary>The picture, redrawn only when the program has moved.</summary>
        Picture = 16,

        /// <summary>The venue's card, redrawn while it is what is up.</summary>
        IdleCard = 32,

        /// <summary>A screen that has just joined: the picture is drawn whatever was last sent.</summary>
        Connected = 64,

        /// <summary>Whether the playing song gets a visualiser; its own load decides it first.</summary>
        Visualiser = 128,

        All = Volume | Marquee | QrCodes | BreakMusicCard | Picture,
    }

    private enum PictureCause
    {
        ProgramMoved,
        VenueChanged,
        Connected,
    }
}
