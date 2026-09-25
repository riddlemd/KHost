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
/// or an ad's still, the song's timed words, and the venue's level. None of that is on
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

    /// <summary>Full volume before any venue exists, so a screen is never silently mute.</summary>
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

    public LocalScreenDisplayProvider(
        ILogger<LocalScreenDisplayProvider> logger,
        IScreenServer screenServer,
        IEnumerable<IScreenProvider> launchers,
        IMessageBroker broker,
        IVenuesService? venuesService = null,
        TimeSpan? registrationTimeout = null,
        IServiceProvider? services = null,
        TimeSpan? redrawSettle = null)
    {
        _logger = logger;
        _screenServer = screenServer;
        _launchers = [.. launchers];
        _broker = broker;
        _venuesService = venuesService;
        _registrationTimeout = registrationTimeout ?? DefaultRegistrationTimeout;
        _services = services;
        _redrawSettle = redrawSettle ?? DefaultRedrawSettle;

        _screenServer.ScreenConnected += OnScreenConnected;
        _screenServer.ScreenDisconnected += OnScreenDisconnected;
        _screenServer.StateReceived += OnStateReceived;

        // The venue owns the level, and everything about how the marquee, the codes and the card
        // look, including whether each is there at all.
        _subscriptions.Add(broker.Subscribe<SelectedVenueChanged>(
            _ => Redraw(Overlay.Volume | Overlay.Marquee | Overlay.QrCodes | Overlay.BreakMusicCard | Overlay.IdleCard)));

        // Who is next is the marquee's content, however the queue, the turns or the mic moved it.
        _subscriptions.Add(broker.Subscribe<UpNextChanged>(_ => Redraw(Overlay.Marquee)));

        // Who is at the mic decides whether a venue hides its codes; what is on the main channel
        // decides the picture.
        _subscriptions.Add(broker.Subscribe<PlaybackChanged>(_ => Redraw(Overlay.QrCodes | Overlay.Picture)));

        // A provider moving to the next track says so apart from a start, pause or hand-off.
        _subscriptions.Add(broker.Subscribe<BreakMusicChanged>(_ => Redraw(Overlay.BreakMusicCard)));
        _subscriptions.Add(broker.Subscribe<BreakMusicTrackChanged>(_ => Redraw(Overlay.BreakMusicCard)));

        // Awaited rather than detached: the owner registering a code is waiting on this publish.
        _subscriptions.Add(broker.Subscribe<QrCodeOfferChanged>((_, _) => RedrawAsync(Overlay.QrCodes)));
        _subscriptions.Add(broker.Subscribe<NextSingerAnnounced>((announced, _) => SendAsync(new ShowNextSingerCommand
        {
            Singer = announced.Card.Singer,
            Song = announced.Card.Song,
            Artist = announced.Card.Artist,
        })));
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
        await SendAsync(ToCommand(load));

        // After the load and before play, which every caller sends after this returns: a screen
        // holds the words until the next load, and one given them mid-song would light every
        // syllable already sung at once.
        await SendTimedLyricsAsync();
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
        => SendAsync(new SetStemVolumeCommand { Role = level.Role, Volume = level.Volume });

    // --- the second audio channel ---

    public Task LoadBackgroundAsync(BackgroundLoad background, CancellationToken cancellationToken = default)
        => SendAsync(new LoadBackgroundCommand { StreamUrl = background.StreamUrl, AutoPlay = background.AutoPlay });

    public Task PlayBackgroundAsync(CancellationToken cancellationToken = default)
        => SendAsync(new PlayBackgroundCommand());

    public Task PauseBackgroundAsync(CancellationToken cancellationToken = default)
        => SendAsync(new PauseBackgroundCommand());

    public Task StopBackgroundAsync(TimeSpan? fade = null, CancellationToken cancellationToken = default)
        => SendAsync(new StopBackgroundCommand { FadeDuration = fade });

    public Task SetBackgroundVolumeAsync(float volume, CancellationToken cancellationToken = default)
        => SendAsync(new SetBackgroundVolumeCommand { Volume = volume });

    // --- plumbing ---

    internal static LoadMediaCommand ToCommand(DisplayLoad load) => new()
    {
        StreamUrl = load.StreamUrl,
        StreamStartOffset = load.StartOffset,
        Tempo = load.Tempo,
        Stems = load.Stems,
    };

    /// <summary>The marquee as the screen draws it, whole, from the venue's settings and who is next.</summary>
    /// <remarks>Disabled with no venue selected, or one that has the marquee off. The singers are
    /// exactly what <see cref="IUpNextService"/> answers for the venue's count, so the band and
    /// anything else naming who is next cannot disagree.</remarks>
    internal static async Task<SetMarqueeCommand> BuildMarqueeAsync(Venue.VenueSettings? settings, IUpNextService upNext)
    {
        if (settings is null || !settings.MarqueeEnabled)
            return new SetMarqueeCommand { Enabled = false };

        var entries = await upNext.ReadAsync(settings.MarqueeSingerCount);

        return new SetMarqueeCommand
        {
            Enabled = true,
            Singers = [.. entries.Select(entry => MarqueeText.ComposeEntry(entry, settings.MarqueeEntryFormat))],
            Message = MarqueeText.CollapseToOneLine(settings.MarqueeMessage),
            Position = settings.MarqueePosition,

            // A cleared colour is no colour, not an empty CSS value the screen would take.
            BackgroundColor = string.IsNullOrWhiteSpace(settings.MarqueeBackgroundColor) ? null : settings.MarqueeBackgroundColor.Trim(),
            TextColor = string.IsNullOrWhiteSpace(settings.MarqueeTextColor) ? null : settings.MarqueeTextColor.Trim(),
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

            await SendAsync(new SetTimedLyricsCommand { Lyrics = _lyrics });
        }
        finally
        {
            _lyricsLock.Release();
        }
    }

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

    /// <summary>The venue's level, or full volume before any venue exists.</summary>
    /// <remarks>The song and the second channel share one venue level: the bed and an ad's own
    /// voiceover ride that channel through one mixer, so one setting covers both.</remarks>
    private async Task ApplyVolumeAsync()
    {
        if (ConnectedScreen() is null) return;

        var volume = FullVolume;

        if (_venuesService is not null)
        {
            try
            {
                var venue = await _venuesService.ReadSelectedVenueAsync();
                if (venue is not null)
                    volume = VenueVolume.ToGain(venue.Settings.DefaultVolume);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read the venue's volume; leaving the screen at full");
            }
        }

        await SendAsync(new SetVolumeCommand { Volume = volume });
        await SendAsync(new SetBackgroundVolumeCommand { Volume = volume });
    }

    /// <summary>Pulls the current state of each overlay asked for and sends it whole.</summary>
    /// <remarks>Never throws: one overlay that cannot be built must not keep the rest off the screen.</remarks>
    private async Task RedrawAsync(Overlay overlays)
    {
        if (overlays.HasFlag(Overlay.Volume))
        {
            try { await ApplyVolumeAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not apply the venue's volume to the screen"); }
        }

        // Ahead of the overlays, which read the database: the picture is what the room notices late.
        if (overlays.HasFlag(Overlay.Connected))
            await DrawPictureAsync(PictureCause.Connected);
        else if (overlays.HasFlag(Overlay.Picture))
            await DrawPictureAsync(PictureCause.ProgramMoved);
        else if (overlays.HasFlag(Overlay.IdleCard))
            await DrawPictureAsync(PictureCause.VenueChanged);

        if (overlays.HasFlag(Overlay.Marquee))
            await DrawAsync<IUpNextService>("marquee", async upNext => await BuildMarqueeAsync(await ReadVenueSettingsAsync(), upNext));

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

            _pictured = program;

            switch (program)
            {
                case PlaybackProgram.AdStill still:
                    await SendAsync(new ShowImageCommand { Url = still.ImageUrl, Scaling = still.Scaling });
                    break;

                // A joiner shows nothing until the song reloads onto it, so there is nothing to take down.
                case PlaybackProgram.Playing when cause == PictureCause.Connected:
                    break;

                case PlaybackProgram.Playing:
                    await SendAsync(new HideImageCommand());
                    break;

                default:
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
                PlaybackStatusChanged?.Invoke(this, new DisplayPlaybackStatus
                {
                    Position = state.Position,
                    IsPlaying = state.IsPlaying,
                    SampledAtUtc = sampledAt,
                });
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

        All = Volume | Marquee | QrCodes | BreakMusicCard | Picture,
    }

    private enum PictureCause
    {
        ProgramMoved,
        VenueChanged,
        Connected,
    }
}
