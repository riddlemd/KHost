using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.Screens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KHost.UnitTests.Domain.Services.Screens;

public class ScreenDisplayProviderTests
{
    private readonly IScreenServer _screenServer = Substitute.For<IScreenServer>();
    private readonly IMessageBroker _broker = Substitute.For<IMessageBroker>();
    private readonly ScreenDisplayProvider _provider;

    // What the screen is drawn from, for the tests that follow a change message to the screen.
    private readonly MessageBroker _realBroker = new(NullLogger<MessageBroker>.Instance);
    private readonly IScreenMarqueeService _marquee = Substitute.For<IScreenMarqueeService>();
    private readonly IScreenQrCodeService _qrCodes = Substitute.For<IScreenQrCodeService>();
    private readonly IBreakMusicCardService _card = Substitute.For<IBreakMusicCardService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();

    public ScreenDisplayProviderTests()
    {
        _marquee.BuildAsync(Arg.Any<CancellationToken>()).Returns(new SetMarqueeCommand { Enabled = true, Message = "Tonight" });
        _qrCodes.BuildAsync(Arg.Any<CancellationToken>()).Returns(new SetScreenQrCodesCommand());
        _card.BuildAsync(Arg.Any<CancellationToken>()).Returns(new SetBreakMusicCardCommand { Enabled = false });
        _venues.ReadSelectedVenueAsync().Returns(new Venue { Name = "The Bar", Settings = new Venue.VenueSettings { DefaultVolume = 50 } });

        _provider = new ScreenDisplayProvider(
            NullLogger<ScreenDisplayProvider>.Instance, _screenServer, [], _broker);
    }

    /// <summary>Wired to a real broker and to what each overlay is built from, as the host wires it.</summary>
    private ScreenDisplayProvider DrawingProvider(IServiceProvider? services = null)
        => new(
            NullLogger<ScreenDisplayProvider>.Instance, _screenServer, [], _realBroker, _venues,
            services: services ?? new ServiceCollection()
                .AddSingleton(_marquee)
                .AddSingleton(_qrCodes)
                .AddSingleton(_card)
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
        Assert.True(device.SupportsLyrics);
        Assert.True(device.SupportsMarquee);
        Assert.True(device.SupportsQrCodes);
        Assert.True(device.SupportsImage);
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
        var provider = new ScreenDisplayProvider(
            NullLogger<ScreenDisplayProvider>.Instance, _screenServer, [launcher], _broker);
        RaiseConnected(Connection("Screen 1", "conn-a"));

        Assert.False(await provider.ConnectAsync("Screen 2"));

        await launcher.DidNotReceive().LaunchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal("Screen 1", provider.ConnectedDeviceId);
    }

    [Fact]
    public async Task ConnectAsync_ToTheScreenAlreadyUp_SucceedsWithoutLaunching()
    {
        var launcher = AvailableLauncher();
        var provider = new ScreenDisplayProvider(
            NullLogger<ScreenDisplayProvider>.Instance, _screenServer, [launcher], _broker);
        RaiseConnected(Connection("Screen 1", "conn-a"));

        Assert.True(await provider.ConnectAsync("Screen 1"));

        await launcher.DidNotReceive().LaunchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartDiscoveryAsync_WhileAScreenIsUp_DoesNotLaunchASecond()
    {
        var launcher = AvailableLauncher();
        var provider = new ScreenDisplayProvider(
            NullLogger<ScreenDisplayProvider>.Instance, _screenServer, [launcher], _broker);
        RaiseConnected(Connection("Screen 1", "conn-a"));

        await provider.StartDiscoveryAsync();

        await launcher.DidNotReceive().LaunchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConnectAsync_WithNoScreenUp_LaunchesTheLocalOne()
    {
        var launcher = AvailableLauncher();
        var provider = new ScreenDisplayProvider(
            NullLogger<ScreenDisplayProvider>.Instance, _screenServer, [launcher], _broker);

        var connectTask = provider.ConnectAsync(ScreenDisplayProvider.LocalScreenId);

        await launcher.Received(1).LaunchAsync(ScreenDisplayProvider.LocalScreenId, Arg.Any<CancellationToken>());

        RaiseConnected(Connection(ScreenDisplayProvider.LocalScreenId, "conn-a"));

        Assert.True(await connectTask.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>The bug this closes: LaunchAsync returns as soon as the process starts, seconds
    /// before the screen registers back. Answering false the instant the launch call returns would
    /// report a connect that was about to succeed as a refusal.</summary>
    [Fact]
    public async Task ConnectAsync_WaitsForTheScreenToRegister_RatherThanReturningAsSoonAsItLaunches()
    {
        var launcher = AvailableLauncher();
        var provider = new ScreenDisplayProvider(
            NullLogger<ScreenDisplayProvider>.Instance, _screenServer, [launcher], _broker);

        var connectTask = provider.ConnectAsync(ScreenDisplayProvider.LocalScreenId);

        // The process has started, but nothing has registered yet: the launch alone must not answer.
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.False(connectTask.IsCompleted);

        RaiseConnected(Connection(ScreenDisplayProvider.LocalScreenId, "conn-a"));

        Assert.True(await connectTask.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>A launch that never registers — the exe missing, the screen crashing on start —
    /// must still resolve ConnectAsync rather than hanging it forever.</summary>
    [Fact]
    public async Task ConnectAsync_GivesUpIfTheScreenNeverRegisters()
    {
        var launcher = AvailableLauncher();
        var provider = new ScreenDisplayProvider(
            NullLogger<ScreenDisplayProvider>.Instance, _screenServer, [launcher], _broker,
            registrationTimeout: TimeSpan.FromMilliseconds(50));

        var connected = await provider.ConnectAsync(ScreenDisplayProvider.LocalScreenId);

        Assert.False(connected);
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
        _realBroker.Announce(new SingerQueueChanged());
        Assert.True(await WaitForSentAsync<SetMarqueeCommand>(marquee => marquee.Message == "Tonight"));

        _marquee.BuildAsync(Arg.Any<CancellationToken>()).Returns(new SetMarqueeCommand { Enabled = true, Message = "Last call" });
        _screenServer.ClearReceivedCalls();

        RaiseConnected(Connection("Screen 1", "conn-a"));

        Assert.True(await WaitForSentAsync<SetMarqueeCommand>(marquee => marquee.Message == "Last call"));
        Assert.DoesNotContain(Sent<SetMarqueeCommand>(), marquee => marquee.Message == "Tonight");
    }

    public static TheoryData<object, bool, bool, bool> WhatEachChangeRedraws() => new()
    {
        // message,                                   marquee, codes, card
        { new SelectedVenueChanged(),                  true,    true,  true  },
        { new SingerQueueChanged(),                    true,    false, false },
        { new PerformancesChanged(),                   true,    false, false },
        { new PlaybackChanged(),                       true,    true,  false },
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
    public async Task ScreenQrCodesChanged_IsDrawnBeforeThePublishReturns()
    {
        using var provider = DrawingProvider();

        await _realBroker.PublishAsync(new ScreenQrCodesChanged());

        Assert.Single(Sent<SetScreenQrCodesCommand>());
    }

    [Fact]
    public async Task NextSingerCardRequested_DrawsThatCard()
    {
        using var provider = DrawingProvider();
        var card = new ShowNextSingerCommand { Singer = "Ada", Song = "Today" };

        await _realBroker.PublishAsync(new NextSingerCardRequested(card));

        Assert.Same(card, Assert.Single(Sent<ShowNextSingerCommand>()));
    }

    /// <summary>One overlay that cannot be built must not keep the rest off a screen that just joined.</summary>
    [Fact]
    public async Task ScreenConnected_AnOverlayThatThrows_DoesNotKeepTheOthersOff()
    {
        _marquee.BuildAsync(Arg.Any<CancellationToken>()).Returns<SetMarqueeCommand>(_ => throw new InvalidOperationException("no venue"));
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

        _realBroker.Announce(new SingerQueueChanged());
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
            .AddSingleton<IScreenQrCodeService>(sp => new ScreenQrCodeService(
                NullLogger<ScreenQrCodeService>.Instance, _venues, sp, _realBroker))
            .BuildServiceProvider();

        using var provider = DrawingProvider(services);
        await services.GetRequiredService<IScreenQrCodeService>().RegisterAsync(new ScreenQrCode
        {
            OwnerId = "example",
            Payload = "https://example.test/",
            Caption = "example",
        });
        _screenServer.ClearReceivedCalls();

        venue.QrCodeCorner = ScreenCorner.TopLeft;
        _realBroker.Announce(new SelectedVenueChanged());

        Assert.True(await WaitForSentAsync<SetScreenQrCodesCommand>(
            codes => codes.Codes.Count == 1 && codes.Codes[0].Corner == ScreenCorner.TopLeft));
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
