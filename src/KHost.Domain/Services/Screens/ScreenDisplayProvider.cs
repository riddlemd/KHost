using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.Screens;

/// <summary>The screens, reached the same way every other display is.</summary>
/// <remarks>Core, not a plugin: this is the host's own transport, registered by the host.
/// <c>PluginLoader</c> must not bind it and it must never appear on the Plugins page — it simply
/// travels the path a plugin's display travels, so <c>PlaybackService</c> has one kind of thing to
/// drive.
///
/// <para>It owns everything the screen shows, not only the song: the marquee, the QR codes, the
/// break music card and the venue's level. The host announces what moved and this pulls the whole
/// current state of whatever that message drives, so a screen that connects is sent everything
/// afresh rather than a replay of what it missed.</para>
///
/// <para>Discovery here is not a sweep. There is nothing to find on a machine: starting discovery
/// launches a screen and it registers back, which is why <see cref="IsDiscovering"/> reports the
/// launch rather than a search.</para></remarks>
public sealed class ScreenDisplayProvider : IDisplayProvider, IStartsWithTheHost, IDisposable
{
    /// <summary>The id a launched screen is given. One at a time, so one name is enough.</summary>
    internal const string LocalScreenId = "Screen 1";

    /// <summary>Full volume before any venue exists, so a screen is never silently mute.</summary>
    private const float FullVolume = 1.0f;

    /// <summary>A screen takes seconds to register once launched; this is how long ConnectAsync
    /// waits for it before reporting a launch that never came back rather than a refusal.</summary>
    private static readonly TimeSpan DefaultRegistrationTimeout = TimeSpan.FromSeconds(10);

    private readonly IScreenServer _screenServer;
    private readonly IReadOnlyList<IScreenProvider> _launchers;
    private readonly IVenuesService? _venuesService;
    private readonly IMessageBroker _broker;
    private readonly SubscriptionSet _subscriptions = new();
    private readonly ILogger<ScreenDisplayProvider> _logger;

    // Resolved on use, never in the constructor: the marquee reaches IPlaybackService, which takes
    // every IDisplayProvider, this one included.
    private readonly IServiceProvider? _services;
    private readonly TimeSpan _registrationTimeout;

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
    /// so nothing has to be asked for.</remarks>
    private volatile IScreenConnection? _connected;

    public ScreenDisplayProvider(
        ILogger<ScreenDisplayProvider> logger,
        IScreenServer screenServer,
        IEnumerable<IScreenProvider> launchers,
        IMessageBroker broker,
        IVenuesService? venuesService = null,
        TimeSpan? registrationTimeout = null,
        IServiceProvider? services = null)
    {
        _logger = logger;
        _screenServer = screenServer;
        _launchers = [.. launchers];
        _broker = broker;
        _venuesService = venuesService;
        _registrationTimeout = registrationTimeout ?? DefaultRegistrationTimeout;
        _services = services;

        _screenServer.ScreenConnected += OnScreenConnected;
        _screenServer.ScreenDisconnected += OnScreenDisconnected;

        // The venue owns the level, and everything about how the marquee, the codes and the card
        // look, including whether each is there at all.
        _subscriptions.Add(broker.Subscribe<SelectedVenueChanged>(
            _ => Redraw(Overlay.Volume | Overlay.Marquee | Overlay.QrCodes | Overlay.BreakMusicCard)));

        // The queue's order is the marquee's content.
        _subscriptions.Add(broker.Subscribe<SingerQueueChanged>(_ => Redraw(Overlay.Marquee)));
        _subscriptions.Add(broker.Subscribe<PerformancesChanged>(_ => Redraw(Overlay.Marquee)));

        // Who is at the mic decides who the band leaves out, and whether a venue hides its codes.
        _subscriptions.Add(broker.Subscribe<PlaybackChanged>(_ => Redraw(Overlay.Marquee | Overlay.QrCodes)));

        // A provider moving to the next track says so apart from a start, pause or hand-off.
        _subscriptions.Add(broker.Subscribe<BreakMusicChanged>(_ => Redraw(Overlay.BreakMusicCard)));
        _subscriptions.Add(broker.Subscribe<BreakMusicTrackChanged>(_ => Redraw(Overlay.BreakMusicCard)));

        // Awaited rather than detached: the owner registering a code is waiting on this publish.
        _subscriptions.Add(broker.Subscribe<ScreenQrCodesChanged>((_, _) => RedrawAsync(Overlay.QrCodes)));
        _subscriptions.Add(broker.Subscribe<NextSingerCardRequested>((request, _) => SendAsync(request.Card)));
    }

    /// <summary>What it is, not where it is. "This computer" read as a location a host might be
    /// choosing between, next to receivers that really are places in the room.</summary>
    public string Name => "Local Display";

    /// <summary>Never raised: a screen reports its own position over IPC, not through here.</summary>
    public event EventHandler<DisplayPlaybackStatus>? PlaybackStatusChanged
    {
        add { } remove { }
    }

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

                    // A screen draws everything the host can put on it. That is what it is for.
                    SupportsAudio = true,
                    SupportsVideo = true,

                    // It owns its own mixer, so a stop rides down instead of cutting.
                    SupportsFade = true,

                    // Both engines behind a screen decode Vorbis, so it can take the stems whole
                    // and ride their levels rather than making the host re-encode to move one.
                    SupportsStemMix = true,
                    SupportsLyrics = true,
                    SupportsMarquee = true,
                    SupportsQrCodes = true,
                    SupportsImage = true,
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

    /// <summary>A screen keeps no session of its own; the host's connection is the session.</summary>
    public Guid? SessionId => null;

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
        foreach (var launcher in _launchers)
        {
            try { launcher.CloseSpawnedScreens(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not close the screen"); }
        }

        Announce();

        return Task.CompletedTask;
    }

    // --- transport ---

    public Task LoadAsync(string streamUrl, TimeSpan startOffset, int tempo = 0, CancellationToken cancellationToken = default)
        => SendAsync(new LoadMediaCommand
        {
            StreamUrl = streamUrl,
            StreamStartOffset = startOffset,
            Tempo = tempo,
        });

    /// <summary>Sent whole, so the stems ride along with it; the page mixes when there are any.</summary>
    public Task LoadAsync(LoadMediaCommand media, CancellationToken cancellationToken = default)
        => SendAsync(media);

    public Task PlayAsync(CancellationToken cancellationToken = default) => SendAsync(new PlayCommand());
    public Task PauseAsync(CancellationToken cancellationToken = default) => SendAsync(new PauseCommand());
    public Task StopAsync(TimeSpan? fade = null, CancellationToken cancellationToken = default)
        => SendAsync(new StopCommand { FadeDuration = fade });

    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
        => SendAsync(new SeekCommand { Position = position });

    public Task SetVolumeAsync(float volume, CancellationToken cancellationToken = default)
        => SendAsync(new SetVolumeCommand { Volume = volume });

    public Task SetStemVolumeAsync(SetStemVolumeCommand stem, CancellationToken cancellationToken = default)
        => SendAsync(stem);

    // --- drawable ---

    public Task SetTimedLyricsAsync(SetTimedLyricsCommand lyrics, CancellationToken cancellationToken = default)
        => SendAsync(lyrics);

    public Task SetMarqueeAsync(SetMarqueeCommand marquee, CancellationToken cancellationToken = default)
        => SendAsync(marquee);

    public Task SetQrCodesAsync(SetScreenQrCodesCommand codes, CancellationToken cancellationToken = default)
        => SendAsync(codes);

    public Task ShowNextSingerAsync(ShowNextSingerCommand card, CancellationToken cancellationToken = default)
        => SendAsync(card);

    public Task SetBreakMusicCardAsync(SetBreakMusicCardCommand card, CancellationToken cancellationToken = default)
        => SendAsync(card);

    public Task ShowImageAsync(ShowImageCommand image, CancellationToken cancellationToken = default)
        => SendAsync(image);

    public Task HideImageAsync(CancellationToken cancellationToken = default)
        => SendAsync(new HideImageCommand());

    public Task SetVideoAsync(bool enabled, CancellationToken cancellationToken = default)
        => SendAsync(new SetVideoCommand { Enabled = enabled });

    // --- the second audio channel ---

    public Task LoadBackgroundAsync(LoadBackgroundCommand background, CancellationToken cancellationToken = default)
        => SendAsync(background);

    public Task PlayBackgroundAsync(CancellationToken cancellationToken = default)
        => SendAsync(new PlayBackgroundCommand());

    public Task PauseBackgroundAsync(CancellationToken cancellationToken = default)
        => SendAsync(new PauseBackgroundCommand());

    public Task StopBackgroundAsync(TimeSpan? fade = null, CancellationToken cancellationToken = default)
        => SendAsync(new StopBackgroundCommand { FadeDuration = fade });

    public Task SetBackgroundVolumeAsync(float volume, CancellationToken cancellationToken = default)
        => SendAsync(new SetBackgroundVolumeCommand { Volume = volume });

    // --- plumbing ---

    /// <summary>A failed send never costs the song: a screen that has gone is not an error here.</summary>
    private async Task SendAsync(IScreenCommand command)
    {
        try { await _screenServer.BroadcastCommandAsync(command); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not send {Command} to the screen", command.GetType().Name); }
    }

    /// <summary>A field read, deliberately: see <see cref="_connected"/>.</summary>
    private IScreenConnection? ConnectedScreen() => _connected;

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

        if (overlays.HasFlag(Overlay.Marquee))
            await DrawAsync<IScreenMarqueeService>("marquee", async marquee => await marquee.BuildAsync());

        // Sent even when there is nothing up: it is the whole state, so it also clears a code left
        // on a screen that dropped and came back.
        if (overlays.HasFlag(Overlay.QrCodes))
            await DrawAsync<IScreenQrCodeService>("QR codes", async codes => await codes.BuildAsync());

        if (overlays.HasFlag(Overlay.BreakMusicCard))
            await DrawAsync<IBreakMusicCardService>("break music card", async card => await card.BuildAsync());
    }

    private async Task DrawAsync<TSource>(string what, Func<TSource, Task<IScreenCommand>> build)
        where TSource : class
    {
        if (_services?.GetService<TSource>() is not { } source) return;

        try { await SendAsync(await build(source)); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not build the {Overlay} for the screen", what); }
    }

    /// <summary>Detached, because broker handlers run one at a time and a redraw reads the database.</summary>
    private void Redraw(Overlay overlays) => _ = Task.Run(() => RedrawAsync(overlays));

    // Raised on the hub thread with a plain EventHandler, so everything here that awaits is
    // detached: awaiting on it would be async void.
    private void OnScreenConnected(object? sender, ScreenConnectionEventArgs e)
    {
        _connected = e.Connection;

        // Answers a ConnectAsync waiting on this launch; a screen that registers without anyone
        // waiting (a relaunch, or one recovering on its own) leaves this null and the call no-ops.
        _registering?.TrySetResult(true);

        Announce();

        // The whole current state, pulled fresh: a screen joining mid-show has been sent none of
        // it, and one that dropped and came back may still be drawing what has since changed.
        Redraw(Overlay.All);
    }

    /// <summary>Matched on the connection, not the screen id: a screen coming back under the same
    /// id is already tracked by the time its old connection's disconnect arrives, and clearing on
    /// the id would take the live one down with the stale one.</summary>
    private void OnScreenDisconnected(object? sender, ScreenConnectionEventArgs e)
    {
        if (_connected?.ConnectionId == e.Connection.ConnectionId)
            _connected = null;

        Announce();
    }

    private void Announce() => _ = Task.Run(() => _broker.PublishAsync(new DisplaysChanged()));

    public void Dispose()
    {
        _screenServer.ScreenConnected -= OnScreenConnected;
        _screenServer.ScreenDisconnected -= OnScreenDisconnected;
        _subscriptions.Dispose();
    }

    [Flags]
    private enum Overlay
    {
        Volume = 1,
        Marquee = 2,
        QrCodes = 4,
        BreakMusicCard = 8,
        All = Volume | Marquee | QrCodes | BreakMusicCard,
    }
}
