using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common.Display;
using KHost.Common.Media;
using KHost.IPC.SignalR.Contracts;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

    private readonly LocalScreenVisualiser _visualiser;

    // Every venue edit and every song redraws the codes, and the picture only changes when the
    // payload does. Encoding a few times a minute for an unchanged string is work for nothing.
    private readonly Dictionary<string, (string Image, int Modules)> _encoded = [];

    // What each overlay last put on this session's screen, as sent. Keyed to the session because
    // a screen that registers again holds nothing and must be sent all of it, unchanged or not.
    private readonly Dictionary<Type, string> _overlaysSent = [];
    private Guid? _overlaysSentOn;

    private readonly SettlingRedraw<Overlay> _redraw;

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
        IOptionsMonitor<PlaybackService.ServiceOptions>? playbackOptions = null,
        TimeProvider? time = null)
    {
        _logger = logger;
        _screenServer = screenServer;
        _launchers = [.. launchers];
        _broker = broker;
        _venuesService = venuesService;
        _registrationTimeout = registrationTimeout ?? DefaultRegistrationTimeout;
        _services = services;
        _playbackOptions = playbackOptions;
        _visualiser = new LocalScreenVisualiser(
            logger, services, SendAsync, song => ReferenceEquals(song, _lyricsFor) ? _lyrics : null, ReadVenueSettingsAsync);
        _redraw = new SettlingRedraw<Overlay>(
            redrawSettle ?? DefaultRedrawSettle, RedrawAsync, ex => _logger.LogWarning(ex, "Could not redraw the screen"), time);

        _screenServer.ScreenConnected += OnScreenConnected;
        _screenServer.ScreenDisconnected += OnScreenDisconnected;
        _screenServer.StateReceived += OnStateReceived;

        // The venue owns how the marquee, the codes and the card look, including whether each is there
        // at all.
        _subscriptions.Add(broker.Subscribe<SelectedVenueChanged>(
            _ => Redraw(Overlay.Marquee | Overlay.QrCodes | Overlay.BreakMusicCard | Overlay.IdleCard | Overlay.Visualiser | Overlay.Theme)));
        // The venue's colours fill in what a song's timing leaves unset, so the words on screen are read again.
        _subscriptions.Add(broker.Subscribe<SelectedVenueChanged>((_, _) => ReplaceTimedLyricsAsync()));

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
        var venue = new VenueRead(ReadSelectedVenueAsync);

        // Ahead of the load, so the venue's card is down before the song's first frame.
        await DrawPictureAsync(PictureCause.ProgramMoved, venue);
        _loadedStreamUrl = load.StreamUrl ?? string.Empty;
        await SendAsync(ToCommand(load, IsGraphicsOnly(_services?.GetService<IPlaybackService>()?.CurrentProgram)));

        // After the load and before play, which every caller sends after this returns: a screen
        // holds the words until the next load, and one given them mid-song would light every
        // syllable already sung at once.
        await SendTimedLyricsAsync();

        // After the words, which it is decided on.
        await _visualiser.SendAsync(load, venue.SettingsAsync);
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

    /// <summary>Hands the screen the "Up next" card with what the venue wants behind it.</summary>
    /// <remarks>A one-shot: a venue edit while it is up reaches the next announcement, not this one.
    /// A visualisation the venue cannot draw (no playlist, an empty one, a preset gone) falls back
    /// to <see cref="NextSingerBackground.Over"/>, what a venue that never chose gets.</remarks>
    private async Task ShowNextSingerAsync(NextSingerCard card)
    {
        var venue = new VenueRead(ReadSelectedVenueAsync);
        var background = (await venue.SettingsAsync())?.NextSingerBackground ?? NextSingerBackground.Over;
        SetVisualiserCommand? visualiser = null;

        if (background == NextSingerBackground.Visualisation)
        {
            visualiser = await _visualiser.ForNextSingerCardAsync(card, venue.SettingsAsync);
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
        var venue = new VenueRead(ReadSelectedVenueAsync);

        if (overlays.HasFlag(Overlay.Volume))
        {
            try { await ApplyVolumeAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not set the screen's volume"); }
        }

        // Ahead of the overlays, which read the database: the picture is what the room notices late.
        if (overlays.HasFlag(Overlay.Connected))
            await DrawPictureAsync(PictureCause.Connected, venue);
        else if (overlays.HasFlag(Overlay.Picture))
            await DrawPictureAsync(PictureCause.ProgramMoved, venue);
        else if (overlays.HasFlag(Overlay.IdleCard))
            await DrawPictureAsync(PictureCause.VenueChanged, venue);

        // Ahead of what it colours, so a screen that has just joined draws them in the venue's colours.
        if (overlays.HasFlag(Overlay.Theme))
        {
            try { await SendOverlayAsync(ScreenOverlayBuilders.BuildScreenTheme(await venue.SettingsAsync())); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not send the venue's colours to the screen"); }
        }

        if (overlays.HasFlag(Overlay.Visualiser))
            await _visualiser.SendAsync(load: null, venue.SettingsAsync);

        if (overlays.HasFlag(Overlay.Marquee))
            await DrawAsync<IUpNextService>("marquee", async upNext =>
                await ScreenOverlayBuilders.BuildMarqueeAsync(await venue.SettingsAsync(), upNext, PerformanceUnderWay()));

        // Sent even when there is nothing up: it is the whole state, so it also clears a code left
        // on a screen that dropped and came back.
        if (overlays.HasFlag(Overlay.QrCodes))
            await DrawAsync<IQrCodeOfferService>("QR codes", async codes => ScreenOverlayBuilders.BuildQrCodes(await codes.ReadOfferAsync(), Encode));

        if (overlays.HasFlag(Overlay.BreakMusicCard))
            await DrawAsync<IBreakMusicService>("break music card", async breakMusic =>
                ScreenOverlayBuilders.BuildBreakMusicCard(await venue.SettingsAsync(), breakMusic));
    }

    /// <summary>The payload's code, encoded once per payload.</summary>
    private (string Image, int Modules) Encode(string payload)
    {
        lock (_encoded)
        {
            if (_encoded.TryGetValue(payload, out var cached))
                return cached;
        }

        var encoded = ScreenOverlayBuilders.EncodeQrCode(payload);

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

    private async Task<Venue?> ReadSelectedVenueAsync()
        => _venuesService is null ? null : await _venuesService.ReadSelectedVenueAsync();

    private async Task<Venue.VenueSettings?> ReadVenueSettingsAsync()
        => (await ReadSelectedVenueAsync())?.Settings;

    /// <summary>Puts up the venue's card, an ad's still, or takes either down for a song.</summary>
    /// <remarks>Drawn only when the program has moved, since PlaybackChanged also means a seek or a
    /// pause. A screen that has just connected gets the picture whatever it was last sent, and a
    /// venue edit redraws only the card: a still over a singer is worse than a stale one.</remarks>
    private async Task DrawPictureAsync(PictureCause cause, VenueRead venue)
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
                    await _visualiser.TakeDownAsync();
                    await SendAsync(new ShowImageCommand { Url = still.ImageUrl, Scaling = still.Scaling });
                    break;

                case PlaybackProgram.Playing:
                    await SendAsync(new HideImageCommand());
                    break;

                default:
                    // Between songs is the venue's card, never the last song's visualiser.
                    await _visualiser.TakeDownAsync();
                    await DrawIdleCardAsync(venue);
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
    private async Task DrawIdleCardAsync(VenueRead read)
    {
        var venue = await read.VenueAsync();

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
    private void Redraw(Overlay overlays) => _redraw.Request(overlays);

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

    /// <summary>The selected venue, read at most once for everything one pass draws.</summary>
    /// <remarks>Unguarded: a pass awaits each draw in turn. A failed read is kept, so each draw
    /// that wanted it fails inside its own guard.</remarks>
    private sealed class VenueRead(Func<Task<Venue?>> read)
    {
        private Task<Venue?>? _venue;

        public Task<Venue?> VenueAsync() => _venue ??= read();

        public async Task<Venue.VenueSettings?> SettingsAsync() => (await VenueAsync())?.Settings;
    }

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

        /// <summary>The venue's colours for everything drawn besides the marquee.</summary>
        Theme = 256,

        All = Volume | Marquee | QrCodes | BreakMusicCard | Picture | Theme,
    }

    private enum PictureCause
    {
        ProgramMoved,
        VenueChanged,
        Connected,
    }
}
