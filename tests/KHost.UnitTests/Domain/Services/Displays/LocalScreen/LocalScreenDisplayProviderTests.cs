using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.IPC.SignalR.Contracts;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.Displays;
using KHost.Domain.Services.Displays.LocalScreen;
using KHost.Domain.Services.QrCodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KHost.UnitTests.Domain.Services.Displays.LocalScreen;

public class LocalScreenDisplayProviderTests
{
    private readonly IScreenServer _screenServer = Substitute.For<IScreenServer>();
    private readonly IMessageBroker _broker = Substitute.For<IMessageBroker>();
    private readonly LocalScreenDisplayProvider _provider;

    // What the screen is drawn from, for the tests that follow a change message to the screen.
    private readonly MessageBroker _realBroker = new(NullLogger<MessageBroker>.Instance);
    private readonly IUpNextService _upNext = Substitute.For<IUpNextService>();
    private readonly Venue.VenueSettings _settings = new() { DefaultVolume = 50, MarqueeEnabled = true, MarqueeMessage = "Tonight" };
    private readonly IQrCodeOfferService _qrCodes = Substitute.For<IQrCodeOfferService>();
    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly IMediaService _library = Substitute.For<IMediaService>();
    private readonly IMediaStreamService _streams = Substitute.For<IMediaStreamService>();
    private readonly ITimedLyricsService _timedLyrics = Substitute.For<ITimedLyricsService>();

    public LocalScreenDisplayProviderTests()
    {
        _upNext.ReadAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<UpNextEntry>)[]);
        _qrCodes.ReadOfferAsync(Arg.Any<CancellationToken>()).Returns((QrCodeOffer?)null);
        _breakMusic.State.Returns(BreakMusicState.Stopped);
        _venues.ReadSelectedVenueAsync().Returns(new Venue { Name = "The Bar", Settings = _settings });
        _playback.CurrentProgram.Returns(new PlaybackProgram.Idle());
        _streams.BuildImageUrl(Arg.Any<Guid>()).Returns(call => $"http://host/media/image/{call.Arg<Guid>()}");

        _provider = new LocalScreenDisplayProvider(
            NullLogger<LocalScreenDisplayProvider>.Instance, _screenServer, [], _broker);
    }

    /// <summary>Wired to a real broker and to what each overlay is built from, as the host wires it.</summary>
    private LocalScreenDisplayProvider DrawingProvider(IServiceProvider? services = null)
        => new(
            NullLogger<LocalScreenDisplayProvider>.Instance, _screenServer, [], _realBroker, _venues,
            services: services ?? new ServiceCollection()
                .AddSingleton(_upNext)
                .AddSingleton(_qrCodes)
                .AddSingleton(_breakMusic)
                .AddSingleton(_playback)
                .AddSingleton(_library)
                .AddSingleton(_streams)
                .AddSingleton(_timedLyrics)
                .BuildServiceProvider());

    private static IScreenConnection Connection(string screenId, string connectionId)
    {
        var connection = Substitute.For<IScreenConnection>();
        connection.ScreenId.Returns(screenId);
        connection.ConnectionId.Returns(connectionId);
        connection.IsConnected.Returns(true);

        return connection;
    }

    private void RaiseConnected(IScreenConnection connection)
        => _screenServer.ScreenConnected += Raise.EventWith(
            _screenServer, new ScreenConnectionEventArgs { Connection = connection });

    private void RaiseDisconnected(IScreenConnection connection)
        => _screenServer.ScreenDisconnected += Raise.EventWith(
            _screenServer, new ScreenConnectionEventArgs { Connection = connection });

    [Fact]
    public void ConnectedDeviceId_IsNull_BeforeAnyScreenArrives()
        => Assert.Null(_provider.ConnectedDeviceId);

    [Fact]
    public void ConnectedDeviceId_NamesTheScreen_OnceItRegisters()
    {
        RaiseConnected(Connection("Screen 1", "conn-a"));

        Assert.Equal("Screen 1", _provider.ConnectedDeviceId);
        Assert.True(Assert.Single(_provider.Devices).IsConnected);
    }

    [Fact]
    public void ConnectedDeviceId_IsNull_AfterTheScreenGoes()
    {
        var screen = Connection("Screen 1", "conn-a");
        RaiseConnected(screen);
        RaiseDisconnected(screen);

        Assert.Null(_provider.ConnectedDeviceId);
        Assert.False(Assert.Single(_provider.Devices).IsConnected);
    }

    /// <summary>A screen coming back under the same id is already tracked by the time its old
    /// connection's disconnect arrives; clearing on the id would take the live one down too.</summary>
    [Fact]
    public void AStaleDisconnect_DoesNotUnseatTheScreenThatReplacedIt()
    {
        RaiseConnected(Connection("Screen 1", "conn-old"));
        RaiseConnected(Connection("Screen 1", "conn-new"));

        RaiseDisconnected(Connection("Screen 1", "conn-old"));

        Assert.Equal("Screen 1", _provider.ConnectedDeviceId);
    }

    /// <summary>The deadlock this class was rewritten to avoid: the server raises its events while
    /// holding the lock a read back would wait on, and these two are read during a Blazor render.
    /// Blocking there kills the circuit outright, with nothing thrown to say why.</summary>
    [Fact]
    public void ReadingTheConnection_NeverAsksTheServer()
    {
        RaiseConnected(Connection("Screen 1", "conn-a"));
        _screenServer.ClearReceivedCalls();

        _ = _provider.ConnectedDeviceId;
        _ = _provider.Devices;

        Assert.Empty(_screenServer.ReceivedCalls());
    }

    [Fact]
    public void Name_SaysWhatItIs_AndTheDeviceSaysWhereItIs()
    {
        Assert.Equal("Local Display", _provider.Name);
        Assert.Equal("This computer", Assert.Single(_provider.Devices).Model);
    }

    /// <summary>A screen owns its own mixer, which is what lets a stop ride down.</summary>
    [Fact]
    public void TheScreen_DeclaresEverythingItCanDo()
    {
        var device = Assert.Single(_provider.Devices);

        Assert.True(device.SupportsAudio);
        Assert.True(device.SupportsVideo);
        Assert.True(device.SupportsFade);
    }

    /// <summary>A screen mixes its own stems and draws its own words, so it wants neither baked in.</summary>
    [Fact]
    public void DescribeTarget_TakesTheStemsAndAsksForNoBurnedWords()
    {
        IDisplayProvider provider = _provider;

        var target = provider.DescribeTarget();

        Assert.True(target.MixesStems);
        Assert.False(target.BurnLyrics);
    }

    /// <summary>There is nothing to find on this machine: the host opens the screen itself.</summary>
    [Fact]
    public void SearchesForDevices_IsFalse()
        => Assert.False(_provider.SearchesForDevices);

    /// <summary>One display at a time: a second screen is refused rather than replacing the first.</summary>
    [Fact]
    public async Task ConnectAsync_WhileADifferentScreenIsUp_IsRefusedWithoutLaunching()
    {
        var launcher = AvailableLauncher();
        var provider = new LocalScreenDisplayProvider(
            NullLogger<LocalScreenDisplayProvider>.Instance, _screenServer, [launcher], _broker);
        RaiseConnected(Connection("Screen 1", "conn-a"));

        Assert.False(await provider.ConnectAsync("Screen 2"));

        await launcher.DidNotReceive().LaunchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal("Screen 1", provider.ConnectedDeviceId);
    }

    [Fact]
    public async Task ConnectAsync_ToTheScreenAlreadyUp_SucceedsWithoutLaunching()
    {
        var launcher = AvailableLauncher();
        var provider = new LocalScreenDisplayProvider(
            NullLogger<LocalScreenDisplayProvider>.Instance, _screenServer, [launcher], _broker);
        RaiseConnected(Connection("Screen 1", "conn-a"));

        Assert.True(await provider.ConnectAsync("Screen 1"));

        await launcher.DidNotReceive().LaunchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartDiscoveryAsync_WhileAScreenIsUp_DoesNotLaunchASecond()
    {
        var launcher = AvailableLauncher();
        var provider = new LocalScreenDisplayProvider(
            NullLogger<LocalScreenDisplayProvider>.Instance, _screenServer, [launcher], _broker);
        RaiseConnected(Connection("Screen 1", "conn-a"));

        await provider.StartDiscoveryAsync();

        await launcher.DidNotReceive().LaunchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConnectAsync_WithNoScreenUp_LaunchesTheLocalOne()
    {
        var launcher = AvailableLauncher();
        var provider = new LocalScreenDisplayProvider(
            NullLogger<LocalScreenDisplayProvider>.Instance, _screenServer, [launcher], _broker);

        var connectTask = provider.ConnectAsync(LocalScreenDisplayProvider.LocalScreenId);

        await launcher.Received(1).LaunchAsync(LocalScreenDisplayProvider.LocalScreenId, Arg.Any<CancellationToken>());

        RaiseConnected(Connection(LocalScreenDisplayProvider.LocalScreenId, "conn-a"));

        Assert.True(await connectTask.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>The bug this closes: LaunchAsync returns as soon as the process starts, seconds
    /// before the screen registers back. Answering false the instant the launch call returns would
    /// report a connect that was about to succeed as a refusal.</summary>
    [Fact]
    public async Task ConnectAsync_WaitsForTheScreenToRegister_RatherThanReturningAsSoonAsItLaunches()
    {
        var launcher = AvailableLauncher();
        var provider = new LocalScreenDisplayProvider(
            NullLogger<LocalScreenDisplayProvider>.Instance, _screenServer, [launcher], _broker);

        var connectTask = provider.ConnectAsync(LocalScreenDisplayProvider.LocalScreenId);

        // The process has started, but nothing has registered yet: the launch alone must not answer.
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.False(connectTask.IsCompleted);

        RaiseConnected(Connection(LocalScreenDisplayProvider.LocalScreenId, "conn-a"));

        Assert.True(await connectTask.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>A launch that never registers — the exe missing, the screen crashing on start —
    /// must still resolve ConnectAsync rather than hanging it forever.</summary>
    [Fact]
    public async Task ConnectAsync_GivesUpIfTheScreenNeverRegisters()
    {
        var launcher = AvailableLauncher();
        var provider = new LocalScreenDisplayProvider(
            NullLogger<LocalScreenDisplayProvider>.Instance, _screenServer, [launcher], _broker,
            registrationTimeout: TimeSpan.FromMilliseconds(50));

        var connected = await provider.ConnectAsync(LocalScreenDisplayProvider.LocalScreenId);

        Assert.False(connected);
    }

    // --- the session and the clock ---

    [Fact]
    public void SessionId_IsNull_BeforeAnyScreenArrives()
        => Assert.Null(_provider.SessionId);

    /// <summary>A screen that re-registers holds nothing, even under the same connection, so the
    /// host must see a new session and hand it the song again.</summary>
    [Fact]
    public void SessionId_IsNew_EachTimeAScreenRegisters()
    {
        RaiseConnected(Connection("Screen 1", "conn-a"));
        var first = _provider.SessionId;

        RaiseConnected(Connection("Screen 1", "conn-a"));

        Assert.NotNull(first);
        Assert.NotNull(_provider.SessionId);
        Assert.NotEqual(first, _provider.SessionId);
    }

    [Fact]
    public void SessionId_IsNull_AfterTheScreenGoes()
    {
        var screen = Connection("Screen 1", "conn-a");
        RaiseConnected(screen);
        RaiseDisconnected(screen);

        Assert.Null(_provider.SessionId);
    }

    [Fact]
    public void SessionId_SurvivesAStaleDisconnect()
    {
        RaiseConnected(Connection("Screen 1", "conn-old"));
        RaiseConnected(Connection("Screen 1", "conn-new"));
        var live = _provider.SessionId;

        RaiseDisconnected(Connection("Screen 1", "conn-old"));

        Assert.Equal(live, _provider.SessionId);
    }

    private void RaiseState(IScreenState state)
        => _screenServer.StateReceived += Raise.EventWith(
            _screenServer, new ScreenStateReceivedEventArgs { ScreenId = "Screen 1", State = state });

    /// <summary>The screen defines the song's clock, reported the way every display reports it.</summary>
    [Fact]
    public void AScreensSampledReport_IsRaisedAsThisDisplaysStatus()
    {
        var sampledAt = DateTime.UtcNow.AddMilliseconds(-300);
        object? from = null;
        DisplayPlaybackStatus? status = null;
        _provider.PlaybackStatusChanged += (sender, reported) => (from, status) = (sender, reported);

        RaiseState(new ScreenPlaybackState
        {
            StreamUrl = "http://host/s.m3u8",
            IsPlaying = true,
            Position = TimeSpan.FromSeconds(42),
            Duration = TimeSpan.FromMinutes(4),
            SampledAtUtc = sampledAt,
        });

        Assert.Same(_provider, from);
        Assert.NotNull(status);
        Assert.Equal(TimeSpan.FromSeconds(42), status.Position);
        Assert.True(status.IsPlaying);
        Assert.Equal(sampledAt, status.SampledAtUtc);
    }

    /// <summary>With no measured offset the report has no anchor, and would move the playhead by
    /// however long it spent in flight.</summary>
    [Fact]
    public void AnUnsampledReport_IsNotPassedOn()
    {
        var raised = 0;
        _provider.PlaybackStatusChanged += (_, _) => raised++;

        RaiseState(new ScreenPlaybackState
        {
            StreamUrl = "http://host/s.m3u8",
            IsPlaying = true,
            Position = TimeSpan.FromSeconds(42),
            Duration = TimeSpan.FromMinutes(4),
            SampledAtUtc = null,
        });

        Assert.Equal(0, raised);
    }

    /// <summary>The bed ending is the second channel's news; the song's clock must not see it.</summary>
    [Fact]
    public void TheBedEnding_RaisesBackgroundTrackEnded_AndNotTheSongsClock()
    {
        var ended = 0;
        var clocked = 0;
        _provider.BackgroundTrackEnded += (_, _) => ended++;
        _provider.PlaybackStatusChanged += (_, _) => clocked++;

        RaiseState(new ScreenBackgroundState { StreamUrl = "http://host/bed.m3u8", IsPlaying = false, HasEnded = true });

        Assert.Equal(1, ended);
        Assert.Equal(0, clocked);
    }

    [Fact]
    public void TheBedStillPlaying_RaisesNothing()
    {
        var ended = 0;
        _provider.BackgroundTrackEnded += (_, _) => ended++;

        RaiseState(new ScreenBackgroundState { StreamUrl = "http://host/bed.m3u8", IsPlaying = true, HasEnded = false });

        Assert.Equal(0, ended);
    }

    [Fact]
    public void Dispose_StopsPassingReportsOn()
    {
        var ended = 0;
        _provider.BackgroundTrackEnded += (_, _) => ended++;
        _provider.Dispose();

        RaiseState(new ScreenBackgroundState { StreamUrl = "http://host/bed.m3u8", IsPlaying = false, HasEnded = true });

        Assert.Equal(0, ended);
    }

    // --- what the screen shows ---

    /// <summary>A screen joining mid-show has been sent nothing, so it is sent everything.</summary>
    [Fact]
    public async Task ScreenConnected_SendsTheWholeCurrentState()
    {
        using var provider = DrawingProvider();

        RaiseConnected(Connection("Screen 1", "conn-a"));

        Assert.True(await WaitForSentAsync<SetMarqueeCommand>());
        Assert.True(await WaitForSentAsync<SetScreenQrCodesCommand>());
        Assert.True(await WaitForSentAsync<SetBreakMusicCardCommand>());
        Assert.True(await WaitForSentAsync<SetVolumeCommand>(volume => volume.Volume < 1.0f));
        Assert.True(await WaitForSentAsync<SetBackgroundVolumeCommand>());
    }

    /// <summary>A message says only that something moved; the screen is sent what is true now.</summary>
    [Fact]
    public async Task ScreenConnected_SendsTheStateAsItIsNow_NotWhatWasLastSent()
    {
        using var provider = DrawingProvider();
        _realBroker.Announce(new UpNextChanged());
        Assert.True(await WaitForSentAsync<SetMarqueeCommand>(marquee => marquee.Message == "Tonight"));

        _settings.MarqueeMessage = "Last call";
        _screenServer.ClearReceivedCalls();

        RaiseConnected(Connection("Screen 1", "conn-a"));

        Assert.True(await WaitForSentAsync<SetMarqueeCommand>(marquee => marquee.Message == "Last call"));
        Assert.DoesNotContain(Sent<SetMarqueeCommand>(), marquee => marquee.Message == "Tonight");
    }

    public static TheoryData<object, bool, bool, bool> WhatEachChangeRedraws() => new()
    {
        // message,                                   marquee, codes, card
        { new SelectedVenueChanged(),                  true,    true,  true  },
        { new UpNextChanged(),                         true,    false, false },

        // Who is next reaches the marquee through UpNextChanged alone, which covers all three.
        { new SingerQueueChanged(),                    false,   false, false },
        { new PerformancesChanged(),                   false,   false, false },
        { new PlaybackChanged(),                       false,   true,  false },
        { new BreakMusicChanged(),                     false,   false, true  },
        { new BreakMusicTrackChanged("Library"),       false,   false, true  },
    };

    /// <summary>Each overlay is resent on exactly what it was always resent on, and nothing else.</summary>
    [Theory]
    [MemberData(nameof(WhatEachChangeRedraws))]
    public async Task AChangeMessage_ResendsTheOverlaysItDrives(object message, bool marquee, bool codes, bool card)
    {
        using var provider = DrawingProvider();

        _realBroker.Announce(message);

        if (marquee) Assert.True(await WaitForSentAsync<SetMarqueeCommand>());
        if (codes) Assert.True(await WaitForSentAsync<SetScreenQrCodesCommand>());
        if (card) Assert.True(await WaitForSentAsync<SetBreakMusicCardCommand>());

        // One redraw sends its overlays in turn, so the ones it owed are in by now; a short grace
        // covers a stray one that was never owed.
        await Task.Delay(50);

        Assert.Equal(marquee, Sent<SetMarqueeCommand>().Any());
        Assert.Equal(codes, Sent<SetScreenQrCodesCommand>().Any());
        Assert.Equal(card, Sent<SetBreakMusicCardCommand>().Any());
    }

    /// <summary>Selecting or editing a venue changes what "audible" means, now rather than on the next connect.</summary>
    [Fact]
    public async Task SelectedVenueChanged_WithAScreenUp_AppliesTheVenuesLevel()
    {
        using var provider = DrawingProvider();
        RaiseConnected(Connection("Screen 1", "conn-a"));
        Assert.True(await WaitForSentAsync<SetBackgroundVolumeCommand>());
        _screenServer.ClearReceivedCalls();

        _venues.ReadSelectedVenueAsync().Returns(new Venue { Name = "The Bar", Settings = new Venue.VenueSettings { DefaultVolume = 100 } });
        _realBroker.Announce(new SelectedVenueChanged());

        Assert.True(await WaitForSentAsync<SetVolumeCommand>(volume => volume.Volume == 1.0f));
    }

    /// <summary>An owner registering a code awaits the publish, so the code is on screen when it returns.</summary>
    [Fact]
    public async Task QrCodeOfferChanged_IsDrawnBeforeThePublishReturns()
    {
        using var provider = DrawingProvider();

        await _realBroker.PublishAsync(new QrCodeOfferChanged());

        Assert.Single(Sent<SetScreenQrCodesCommand>());
    }

    [Fact]
    public async Task NextSingerAnnounced_DrawsThatCard()
    {
        using var provider = DrawingProvider();

        await _realBroker.PublishAsync(new NextSingerAnnounced(new NextSingerCard { Singer = "Ada", Song = "Today", Artist = "Pogues" }));

        var drawn = Assert.Single(Sent<ShowNextSingerCommand>());
        Assert.Equal(("Ada", "Today", "Pogues"), (drawn.Singer, drawn.Song, drawn.Artist));
    }

    /// <summary>One overlay that cannot be built must not keep the rest off a screen that just joined.</summary>
    [Fact]
    public async Task ScreenConnected_AnOverlayThatThrows_DoesNotKeepTheOthersOff()
    {
        _upNext.ReadAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<UpNextEntry>>(_ => throw new InvalidOperationException("no queue"));
        using var provider = DrawingProvider();

        RaiseConnected(Connection("Screen 1", "conn-a"));

        Assert.True(await WaitForSentAsync<SetScreenQrCodesCommand>());
        Assert.True(await WaitForSentAsync<SetBreakMusicCardCommand>());
    }

    /// <summary>Disposing must release the broker, or a rebuilt provider leaves the old one drawing.</summary>
    [Fact]
    public async Task Dispose_StopsRedrawing()
    {
        var provider = DrawingProvider();
        provider.Dispose();

        _realBroker.Announce(new UpNextChanged());
        await Task.Delay(50);

        Assert.Empty(Sent<SetMarqueeCommand>());
    }

    /// <summary>Through the real code service: a venue moving its codes reaches the screen on its own.</summary>
    [Fact]
    public async Task SelectedVenueChanged_RedrawsWhereTheCodesSit()
    {
        var venue = new Venue.VenueSettings { QrCodeSource = "example" };
        _venues.ReadSelectedVenueAsync().Returns(_ => new Venue { Name = "The Bar", Settings = venue });

        var services = new ServiceCollection()
            .AddSingleton(_venues)
            .AddSingleton(Substitute.For<IPlaybackService>())
            .AddSingleton<IMessageBroker>(_realBroker)
            .AddSingleton<IQrCodeService>(sp => new QrCodeService(
                NullLogger<QrCodeService>.Instance, _venues, sp, _realBroker))
            .AddSingleton<IQrCodeOfferService>(sp => sp.GetRequiredService<IQrCodeService>())
            .BuildServiceProvider();

        using var provider = DrawingProvider(services);
        await services.GetRequiredService<IQrCodeService>().RegisterAsync(new QrCodeRegistration
        {
            OwnerId = "example",
            Payload = "https://example.test/",
            Caption = "example",
        });
        _screenServer.ClearReceivedCalls();

        venue.QrCodeCorner = OverlayCorner.TopLeft;
        _realBroker.Announce(new SelectedVenueChanged());

        Assert.True(await WaitForSentAsync<SetScreenQrCodesCommand>(
            codes => codes.Codes.Count == 1 && codes.Codes[0].Corner == OverlayCorner.TopLeft));
    }

    // --- the QR code ---

    private static QrCodeOffer Offer(string payload = "https://example.test/join", string? caption = "Scan me") => new()
    {
        Payload = payload,
        Caption = caption,
    };

    /// <summary>The single code the screen was last sent, or null when it was sent none.</summary>
    private async Task<ScreenQrCodePlacement?> DrawnCodeAsync(LocalScreenDisplayProvider provider, QrCodeOffer? offer)
    {
        _qrCodes.ReadOfferAsync(Arg.Any<CancellationToken>()).Returns(offer);

        await _realBroker.PublishAsync(new QrCodeOfferChanged());

        return Sent<SetScreenQrCodesCommand>().Last().Codes.SingleOrDefault();
    }

    /// <summary>Sent even with nothing offered: it is the whole state, and clears a code left up.</summary>
    [Fact]
    public async Task NoOffer_SendsAnEmptySet()
    {
        using var provider = DrawingProvider();

        Assert.Null(await DrawnCodeAsync(provider, null));
        Assert.Single(Sent<SetScreenQrCodesCommand>());
    }

    [Fact]
    public async Task AnOffer_CarriesItsCaption()
    {
        using var provider = DrawingProvider();

        Assert.Equal("Scan me", (await DrawnCodeAsync(provider, Offer()))?.Caption);
    }

    /// <summary>A venue that has never been asked still has to put a code somewhere sensible.</summary>
    [Fact]
    public async Task AnOfferWithNoPlacement_LandsBottomRightAtMediumWithTheHostsOwnInset()
    {
        using var provider = DrawingProvider();

        var placed = Assert.IsType<ScreenQrCodePlacement>(await DrawnCodeAsync(provider, Offer()));

        Assert.Equal(OverlayCorner.BottomRight, placed.Corner);
        Assert.Equal(QrCodeSize.Medium, placed.Size);
        Assert.Equal(1, placed.SafeZone);
        Assert.Equal(0.2, placed.Offset);
    }

    /// <summary>It is the venue's screen, so its choice stands over the fallback.</summary>
    [Fact]
    public async Task AnOfferWithAPlacement_IsDrawnWhereTheVenueSaid()
    {
        using var provider = DrawingProvider();

        var placed = Assert.IsType<ScreenQrCodePlacement>(await DrawnCodeAsync(provider, Offer() with
        {
            Corner = OverlayCorner.TopLeft,
            Size = QrCodeSize.Large,
            SafeZone = 4,
            Offset = 3.5,
        }));

        Assert.Equal(OverlayCorner.TopLeft, placed.Corner);
        Assert.Equal(QrCodeSize.Large, placed.Size);
        Assert.Equal(4, placed.SafeZone);
        Assert.Equal(3.5, placed.Offset);
    }

    /// <summary>SVG, not pixels: in a corner a few centimetres across, module edges decide whether a phone reads it.</summary>
    [Fact]
    public async Task AnOffer_IsDrawnAsAVector()
    {
        using var provider = DrawingProvider();

        var placed = Assert.IsType<ScreenQrCodePlacement>(await DrawnCodeAsync(provider, Offer()));

        Assert.StartsWith("data:image/svg+xml;base64,", placed.ImageUrl);
        Assert.Contains("<svg", Decode(placed.ImageUrl), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The picture carries no quiet zone, so module count is what is actually drawn.</summary>
    [Fact]
    public async Task TheModuleCount_IsWhatTheImageDraws()
    {
        using var provider = DrawingProvider();

        var placed = Assert.IsType<ScreenQrCodePlacement>(await DrawnCodeAsync(provider, Offer()));
        var svg = Decode(placed.ImageUrl);

        // One unit per module, so the SVG's declared size is the module count.
        Assert.Contains($"width=\"{placed.Modules}\"", svg);
        Assert.Contains($"height=\"{placed.Modules}\"", svg);

        // 21 modules is the smallest a QR can be, before its four-module quiet zone.
        Assert.True(placed.Modules >= 21, $"Expected a real module count, got {placed.Modules}");
    }

    /// <summary>A longer payload needs more modules, the reason the count is sent at all.</summary>
    [Fact]
    public async Task ALongerPayload_NeedsMoreModules()
    {
        using var provider = DrawingProvider();

        var shortCode = Assert.IsType<ScreenQrCodePlacement>(await DrawnCodeAsync(provider, Offer("https://k.test/a")));
        var longCode = Assert.IsType<ScreenQrCodePlacement>(await DrawnCodeAsync(provider,
            Offer("https://app.example.com/remote/join?channel=" + new string('x', 180))));

        Assert.True(longCode.Modules > shortCode.Modules);
    }

    [Fact]
    public async Task TheSamePayloadTwice_DrawsTheSameCode()
    {
        using var provider = DrawingProvider();

        var first = Assert.IsType<ScreenQrCodePlacement>(await DrawnCodeAsync(provider, Offer("https://k.test/same")));
        var second = Assert.IsType<ScreenQrCodePlacement>(await DrawnCodeAsync(provider, Offer("https://k.test/same")));

        Assert.Equal(first.ImageUrl, second.ImageUrl);
    }

    private static string Decode(string dataUri)
        => System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(dataUri["data:image/svg+xml;base64,".Length..]));

    // --- the break music card ---

    private void ArrangeBreakMusic(
        bool enabled = true,
        BreakMusicState state = BreakMusicState.Playing,
        string? title = "Free Fallin'",
        string artist = "Tom Petty",
        OverlayCorner? corner = null,
        double offset = 0)
    {
        _venues.ReadSelectedVenueAsync().Returns(new Venue
        {
            Name = "The Bar",
            Settings = new Venue.VenueSettings
            {
                BreakMusicCardEnabled = enabled,
                BreakMusicCardCorner = corner,
                QrCodeOffset = offset,
            },
        });

        _breakMusic.State.Returns(state);
        _breakMusic.CurrentTrack.Returns(title is null ? null : new BreakMusicTrack { Title = title, Artist = artist });
    }

    private async Task<SetBreakMusicCardCommand> DrawnCardAsync()
    {
        using var provider = DrawingProvider();

        _realBroker.Announce(new BreakMusicChanged());

        Assert.True(await WaitForSentAsync<SetBreakMusicCardCommand>());
        return Assert.Single(Sent<SetBreakMusicCardCommand>());
    }

    [Fact]
    public async Task BreakMusicCard_Playing_NamesTheTrackAndTheArtist()
    {
        ArrangeBreakMusic();

        var card = await DrawnCardAsync();

        Assert.True(card.Enabled);
        Assert.Equal("Free Fallin'", card.Title);
        Assert.Equal("Tom Petty", card.Artist);
    }

    /// <summary>Every state where the room hears something else takes the card down: a paused
    /// host meant it, and Suspended is break music standing aside for a singer.</summary>
    [Theory]
    [InlineData(BreakMusicState.Paused)]
    [InlineData(BreakMusicState.Suspended)]
    [InlineData(BreakMusicState.Stopped)]
    public async Task BreakMusicCard_NotPlaying_SaysNothing(BreakMusicState state)
    {
        ArrangeBreakMusic(state: state);

        Assert.False((await DrawnCardAsync()).Enabled);
    }

    /// <summary>The venue's choice beats whatever is playing.</summary>
    [Fact]
    public async Task BreakMusicCard_VenueTurnedItOff_SaysNothingWhilePlaying()
    {
        ArrangeBreakMusic(enabled: false);

        Assert.False((await DrawnCardAsync()).Enabled);
    }

    /// <summary>Off for a venue never asked, so the missing setting needed no backfill.</summary>
    [Fact]
    public async Task BreakMusicCard_VenueNeverAsked_SaysNothingWhilePlaying()
    {
        ArrangeBreakMusic();
        _venues.ReadSelectedVenueAsync().Returns(new Venue { Name = "The Bar", Settings = new Venue.VenueSettings() });

        Assert.False((await DrawnCardAsync()).Enabled);
    }

    /// <summary>No venue is nobody to have asked, so it is the same answer rather than a default.</summary>
    [Fact]
    public async Task BreakMusicCard_NoVenueSelected_SaysNothing()
    {
        ArrangeBreakMusic();
        _venues.ReadSelectedVenueAsync().Returns((Venue?)null);

        Assert.False((await DrawnCardAsync()).Enabled);
    }

    /// <summary>A provider driving another app need not report an artist.</summary>
    [Fact]
    public async Task BreakMusicCard_NoArtistReported_NamesTheTrackAlone()
    {
        ArrangeBreakMusic(artist: "");

        var card = await DrawnCardAsync();

        Assert.True(card.Enabled);
        Assert.Null(card.Artist);
    }

    /// <summary>A provider with nothing to say has nothing worth a corner of the picture.</summary>
    [Fact]
    public async Task BreakMusicCard_NoTitleReported_SaysNothing()
    {
        ArrangeBreakMusic(title: "");

        Assert.False((await DrawnCardAsync()).Enabled);
    }

    /// <summary>Away from the codes' own default, so the two do not share a corner uninvited.</summary>
    [Fact]
    public async Task BreakMusicCard_VenueNeverChoseACorner_TakesBottomLeft()
    {
        ArrangeBreakMusic();

        Assert.Equal(OverlayCorner.BottomLeft, (await DrawnCardAsync()).Corner);
    }

    [Fact]
    public async Task BreakMusicCard_VenueChoseACorner_UsesIt()
    {
        ArrangeBreakMusic(corner: OverlayCorner.TopRight);

        Assert.Equal(OverlayCorner.TopRight, (await DrawnCardAsync()).Corner);
    }

    /// <summary>The inset belongs to the corner, not what sits in it: a card and a code must agree.</summary>
    [Fact]
    public async Task BreakMusicCard_VenueSetAnInset_SharesItWithTheCodes()
    {
        ArrangeBreakMusic(offset: 6.5);

        Assert.Equal(6.5, (await DrawnCardAsync()).Offset, 3);
    }

    [Fact]
    public async Task BreakMusicCard_VenueNeverSetAnInset_TakesTheHostsOwn()
    {
        ArrangeBreakMusic(offset: 0);

        Assert.Equal(0.2, (await DrawnCardAsync()).Offset, 3);
    }

    // --- the picture ---

    private static readonly PlaybackProgram.AdStill Still = new("http://host/media/image/ad", ImageScaling.Fill);

    private static PlaybackProgram.Playing Song() => new(new Media { Title = "Africa", FilePath = "/africa.mp4" }, new Performance());

    [Fact]
    public async Task PlaybackChanged_ToAStill_ShowsItWithItsScaling()
    {
        using var provider = DrawingProvider();
        _playback.CurrentProgram.Returns(Still);

        _realBroker.Announce(new PlaybackChanged());

        Assert.True(await WaitForSentAsync<ShowImageCommand>(
            image => image.Url == Still.ImageUrl && image.Scaling == ImageScaling.Fill));
    }

    /// <summary>PlaybackChanged is also a seek or a pause; the picture moves only with the program.</summary>
    [Fact]
    public async Task PlaybackChanged_ProgramUnmoved_RedrawsNothing()
    {
        using var provider = DrawingProvider();
        _playback.CurrentProgram.Returns(Still);
        _realBroker.Announce(new PlaybackChanged());
        Assert.True(await WaitForSentAsync<ShowImageCommand>());
        _screenServer.ClearReceivedCalls();

        _realBroker.Announce(new PlaybackChanged());
        Assert.True(await WaitForSentAsync<SetScreenQrCodesCommand>());
        await Task.Delay(50);

        Assert.Empty(Sent<ShowImageCommand>());
    }

    [Fact]
    public async Task PlaybackChanged_ToASong_TakesThePictureDown()
    {
        using var provider = DrawingProvider();
        _playback.CurrentProgram.Returns(Song());

        _realBroker.Announce(new PlaybackChanged());

        Assert.True(await WaitForSentAsync<HideImageCommand>());
    }

    /// <summary>The same picture cards two rooms of different shapes; the venue's scaling wins.</summary>
    [Fact]
    public async Task PlaybackChanged_ToIdle_PutsUpTheVenuesCard()
    {
        var card = Branding(venueScaling: ImageScaling.Stretch);
        using var provider = DrawingProvider();
        _playback.CurrentProgram.Returns(Song());
        _realBroker.Announce(new PlaybackChanged());
        Assert.True(await WaitForSentAsync<HideImageCommand>());

        _playback.CurrentProgram.Returns(new PlaybackProgram.Idle());
        _realBroker.Announce(new PlaybackChanged());

        Assert.True(await WaitForSentAsync<ShowImageCommand>(
            image => image.Url.Contains(card.ToString()) && image.Scaling == ImageScaling.Stretch));
    }

    [Fact]
    public async Task PlaybackChanged_ToIdle_WithNoCard_ClearsThePicture()
    {
        using var provider = DrawingProvider();
        _playback.CurrentProgram.Returns(Still);
        _realBroker.Announce(new PlaybackChanged());
        Assert.True(await WaitForSentAsync<ShowImageCommand>());

        _playback.CurrentProgram.Returns(new PlaybackProgram.Idle());
        _realBroker.Announce(new PlaybackChanged());

        Assert.True(await WaitForSentAsync<HideImageCommand>());
    }

    /// <summary>A branding row pointing at a song would reach the screen as a URL serving nothing.</summary>
    [Fact]
    public async Task TheVenuesCard_NotAnImage_ClearsThePictureInstead()
    {
        Branding(format: "MP4");
        using var provider = DrawingProvider();

        _realBroker.Announce(new PlaybackChanged());

        Assert.True(await WaitForSentAsync<HideImageCommand>());
        Assert.Empty(Sent<ShowImageCommand>());
    }

    /// <summary>An edit to the venue's card shows now, not at the next transition.</summary>
    [Fact]
    public async Task SelectedVenueChanged_WhileIdle_RedrawsTheCard()
    {
        using var provider = DrawingProvider();
        _realBroker.Announce(new PlaybackChanged());
        Assert.True(await WaitForSentAsync<HideImageCommand>());

        var card = Branding();
        _realBroker.Announce(new SelectedVenueChanged());

        Assert.True(await WaitForSentAsync<ShowImageCommand>(image => image.Url.Contains(card.ToString())));
    }

    /// <summary>A card over a singer is worse than a stale one.</summary>
    [Fact]
    public async Task SelectedVenueChanged_WhileASongIsOn_LeavesThePictureAlone()
    {
        Branding();
        _playback.CurrentProgram.Returns(Song());
        using var provider = DrawingProvider();

        _realBroker.Announce(new SelectedVenueChanged());
        Assert.True(await WaitForSentAsync<SetBreakMusicCardCommand>());
        await Task.Delay(50);

        Assert.Empty(Sent<ShowImageCommand>());
        Assert.Empty(Sent<HideImageCommand>());
    }

    /// <summary>A joiner is drawn what is up now, however recently the provider last sent it.</summary>
    [Fact]
    public async Task ScreenConnected_RedrawsTheStillAlreadySent()
    {
        using var provider = DrawingProvider();
        _playback.CurrentProgram.Returns(Still);
        _realBroker.Announce(new PlaybackChanged());
        Assert.True(await WaitForSentAsync<ShowImageCommand>());
        _screenServer.ClearReceivedCalls();

        RaiseConnected(Connection("Screen 1", "conn-a"));

        Assert.True(await WaitForSentAsync<ShowImageCommand>(image => image.Url == Still.ImageUrl));
    }

    [Fact]
    public async Task ScreenConnected_WhileIdle_PutsUpTheVenuesCard()
    {
        var card = Branding();
        using var provider = DrawingProvider();

        RaiseConnected(Connection("Screen 1", "conn-a"));

        Assert.True(await WaitForSentAsync<ShowImageCommand>(image => image.Url.Contains(card.ToString())));
    }

    /// <summary>A joiner mid-song shows nothing until the song reloads onto it.</summary>
    [Fact]
    public async Task ScreenConnected_DuringASong_SendsNoPicture()
    {
        _playback.CurrentProgram.Returns(Song());
        using var provider = DrawingProvider();

        RaiseConnected(Connection("Screen 1", "conn-a"));
        Assert.True(await WaitForSentAsync<SetBreakMusicCardCommand>());

        Assert.Empty(Sent<HideImageCommand>());
        Assert.Empty(Sent<ShowImageCommand>());
    }

    // --- the host's calls, as the screen's own commands ---

    /// <summary>Every field the screen keeps its clock and its mixer by; one dropped here plays the
    /// song but reports positions against the wrong zero or rate.</summary>
    [Fact]
    public async Task LoadAsync_HandsTheScreenEverythingTheHostLoaded()
    {
        StemSource[] stems = [new(0, AudioTrackRole.Music, "http://host/m.ogg", 100), new(1, AudioTrackRole.Lead, "http://host/l.ogg", 30)];

        await _provider.LoadAsync(new DisplayLoad
        {
            StreamUrl = "http://host/s.m3u8",
            StartOffset = TimeSpan.FromSeconds(42),
            Tempo = -20,
            Stems = stems,
        });

        var load = Assert.Single(Sent<LoadMediaCommand>());
        Assert.Equal("http://host/s.m3u8", load.StreamUrl);
        Assert.Equal(TimeSpan.FromSeconds(42), load.StreamStartOffset);
        Assert.Equal(-20, load.Tempo);
        Assert.Equal(stems, load.Stems);
    }

    /// <summary>Stems alone: nothing was encoded, and the screen must not be handed a stream name.</summary>
    [Fact]
    public async Task LoadAsync_StemsOnly_SendsNoStream()
    {
        await _provider.LoadAsync(new DisplayLoad { Stems = [new(0, AudioTrackRole.Music, "http://host/m.ogg", 100)] });

        Assert.Null(Assert.Single(Sent<LoadMediaCommand>()).StreamUrl);
    }

    [Fact]
    public async Task SetStemVolumeAsync_RidesTheLevelOnTheScreen()
    {
        var taken = await _provider.SetStemVolumeAsync(new StemLevel { Role = AudioTrackRole.Backing, Volume = 35 });

        Assert.True(taken);
        var level = Assert.Single(Sent<SetStemVolumeCommand>());
        Assert.Equal((AudioTrackRole.Backing, 35), (level.Role, level.Volume));
    }

    /// <summary>A send that never landed is a level the room never heard; saying so has the host
    /// carry it in a rebuilt stream instead.</summary>
    [Fact]
    public async Task SetStemVolumeAsync_TheSendFails_SaysItWasNotTaken()
    {
        _screenServer.BroadcastCommandAsync(Arg.Any<IScreenCommand>()).Returns(_ => throw new InvalidOperationException("gone"));

        Assert.False(await _provider.SetStemVolumeAsync(new StemLevel { Role = AudioTrackRole.Lead, Volume = 10 }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LoadBackgroundAsync_HandsTheScreenTheBed(bool autoPlay)
    {
        await _provider.LoadBackgroundAsync(new BackgroundLoad { StreamUrl = "http://host/bed.m3u8", AutoPlay = autoPlay });

        var bed = Assert.Single(Sent<LoadBackgroundCommand>());
        Assert.Equal(("http://host/bed.m3u8", autoPlay), (bed.StreamUrl, bed.AutoPlay));
    }

    /// <summary>The card comes down ahead of the song's first frame, not after it.</summary>
    [Fact]
    public async Task LoadAsync_ANewProgram_TakesThePictureDownBeforeTheLoad()
    {
        using var provider = DrawingProvider();
        _playback.CurrentProgram.Returns(Song());

        await provider.LoadAsync(new DisplayLoad { StreamUrl = "http://host/s.m3u8" });

        var sent = _screenServer.ReceivedCalls().Select(call => call.GetArguments()[0]).ToList();
        Assert.True(sent.FindIndex(c => c is HideImageCommand) is >= 0 and var hide
            && hide < sent.FindIndex(c => c is LoadMediaCommand));
    }

    /// <summary>A rebuild at a new key reloads the same program; the picture is already right.</summary>
    [Fact]
    public async Task LoadAsync_TheSameProgramAgain_LeavesThePictureAlone()
    {
        using var provider = DrawingProvider();
        _playback.CurrentProgram.Returns(Song());
        await provider.LoadAsync(new DisplayLoad { StreamUrl = "http://host/s.m3u8" });
        _screenServer.ClearReceivedCalls();

        await provider.LoadAsync(new DisplayLoad { StreamUrl = "http://host/s2.m3u8" });

        Assert.Empty(Sent<HideImageCommand>());
        Assert.Single(Sent<LoadMediaCommand>());
    }

    // --- the words ---

    private static readonly DisplayLoad ALoad = new() { StreamUrl = "http://host/s.m3u8" };

    private TimedLyrics WordsFor(PlaybackProgram.Playing song)
    {
        var words = new TimedLyrics { DurationSeconds = 90, Bounds = new LyricBox(0, 0, 640, 360) };
        _timedLyrics.GetTimedLyricsAsync(song.Media.FilePath, Arg.Any<CancellationToken>()).Returns(words);
        return words;
    }

    /// <summary>After the load and before play: given mid-song, every syllable already sung lights at once.</summary>
    [Fact]
    public async Task LoadAsync_ASong_SendsItsWordsRightAfterTheLoad()
    {
        var song = Song();
        var words = WordsFor(song);
        _playback.CurrentProgram.Returns(song);
        using var provider = DrawingProvider();

        await provider.LoadAsync(ALoad);

        var sent = _screenServer.ReceivedCalls().Select(call => call.GetArguments()[0]).ToList();
        var load = sent.FindIndex(c => c is LoadMediaCommand);
        var lyrics = sent.FindIndex(c => c is SetTimedLyricsCommand command && ReferenceEquals(command.Lyrics, words));
        Assert.True(load >= 0 && lyrics > load, "The words did not follow the load.");
    }

    /// <summary>Skipping the send leaves the last song's words lit over this one.</summary>
    [Fact]
    public async Task LoadAsync_ASongWithNoWords_StillClearsTheLastSongs()
    {
        _playback.CurrentProgram.Returns(Song());
        _timedLyrics.GetTimedLyricsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((TimedLyrics?)null);
        using var provider = DrawingProvider();

        await provider.LoadAsync(ALoad);

        Assert.Null(Assert.Single(Sent<SetTimedLyricsCommand>()).Lyrics);
    }

    /// <summary>A plugin that cannot read its own file costs the words, never the song.</summary>
    [Fact]
    public async Task LoadAsync_TheTimingCannotBeRead_LoadsAndClearsTheWords()
    {
        _playback.CurrentProgram.Returns(Song());
        _timedLyrics.GetTimedLyricsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<TimedLyrics?>>(_ => throw new InvalidDataException("bad timing"));
        using var provider = DrawingProvider();

        await provider.LoadAsync(ALoad);

        Assert.Single(Sent<LoadMediaCommand>());
        Assert.Null(Assert.Single(Sent<SetTimedLyricsCommand>()).Lyrics);
    }

    /// <summary>An ad is nobody's song and has no words, as it never had.</summary>
    [Fact]
    public async Task LoadAsync_AVideoAd_SendsNoWords()
    {
        _playback.CurrentProgram.Returns(new PlaybackProgram.Playing(new Media { Title = "Ad", FilePath = "/ad.mp4" }, null));
        using var provider = DrawingProvider();

        await provider.LoadAsync(ALoad);

        Assert.Empty(Sent<SetTimedLyricsCommand>());
        await _timedLyrics.DidNotReceive().GetTimedLyricsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A rebuild at a new key reloads the same song onto a screen still holding its words.</summary>
    [Fact]
    public async Task LoadAsync_TheSameSongOnTheSameScreen_SendsTheWordsOnce()
    {
        var song = Song();
        WordsFor(song);
        _playback.CurrentProgram.Returns(song);
        using var provider = DrawingProvider();
        RaiseConnected(Connection("Screen 1", "conn-a"));

        await provider.LoadAsync(ALoad);
        await provider.LoadAsync(ALoad);

        Assert.Single(Sent<SetTimedLyricsCommand>());
    }

    /// <summary>A screen that came back holds nothing, even under the same connection id.</summary>
    [Fact]
    public async Task LoadAsync_TheSameSongOntoAScreenThatRejoined_ResendsWithoutRereading()
    {
        var song = Song();
        var words = WordsFor(song);
        _playback.CurrentProgram.Returns(song);
        using var provider = DrawingProvider();
        RaiseConnected(Connection("Screen 1", "conn-a"));
        await provider.LoadAsync(ALoad);

        RaiseConnected(Connection("Screen 1", "conn-a"));
        await provider.LoadAsync(ALoad);

        Assert.Equal(2, Sent<SetTimedLyricsCommand>().Count(command => ReferenceEquals(command.Lyrics, words)));
        await _timedLyrics.Received(1).GetTimedLyricsAsync(song.Media.FilePath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAsync_ANewSong_SendsItsOwnWords()
    {
        var first = Song();
        var second = Song() with { Media = new Media { Title = "Rosanna", FilePath = "/rosanna.mp4" } };
        WordsFor(first);
        var secondWords = WordsFor(second);
        using var provider = DrawingProvider();

        _playback.CurrentProgram.Returns(first);
        await provider.LoadAsync(ALoad);
        _playback.CurrentProgram.Returns(second);
        await provider.LoadAsync(ALoad);

        Assert.Same(secondWords, Sent<SetTimedLyricsCommand>().Last().Lyrics);
    }

    /// <summary>A venue whose card is an image in the library.</summary>
    private Guid Branding(ImageScaling? venueScaling = null, string format = "PNG")
    {
        var id = Guid.NewGuid();

        _venues.ReadSelectedVenueAsync().Returns(new Venue
        {
            Name = "The Bar",
            Settings = new Venue.VenueSettings { BrandingImageMediaId = id, BrandingImageScaling = venueScaling },
        });
        _library.ReadAsync(id).Returns(new Media { Id = id, Title = "Card", FilePath = "/card.png", Format = format, ImageScaling = ImageScaling.Original });

        return id;
    }

    private List<TCommand> Sent<TCommand>() where TCommand : IScreenCommand
        => [.. _screenServer.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IScreenServer.BroadcastCommandAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<TCommand>()];

    // Redraws leave the broker's thread, so an assertion straight after an announce races them.
    private async Task<bool> WaitForSentAsync<TCommand>(Func<TCommand, bool>? matches = null)
        where TCommand : IScreenCommand
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (Sent<TCommand>().Any(command => matches?.Invoke(command) ?? true))
                return true;

            await Task.Delay(10);
        }

        return false;
    }

    private static IScreenProvider AvailableLauncher()
    {
        var launcher = Substitute.For<IScreenProvider>();
        launcher.IsAvailable.Returns(true);

        return launcher;
    }
}
