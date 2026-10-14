using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Services;

/// <summary>One display at a time across every provider: whatever holds the song lets go before
/// another display takes it.</summary>
public class DisplaySwitcherTests
{
    private readonly IDisplayProvider _screens = Display("Local Display", searches: false);
    private readonly IDisplayProvider _cast = Display("Chromecast", searches: true);
    private readonly IDisplayProvider _airPlay = Display("AirPlay", searches: true);

    private static IDisplayProvider Display(string name, bool searches)
    {
        var display = Substitute.For<IDisplayProvider>();
        display.Name.Returns(name);
        display.SearchesForDevices.Returns(searches);
        // NSubstitute answers "" for an unstubbed string; null says nothing is connected.
        display.ConnectedDeviceId.Returns((string?)null);
        display.Devices.Returns([Device(name + "-device")]);
        display.ConnectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        return display;
    }

    private static DisplayDevice Device(string id) => new() { Id = id, Name = id };

    private DisplaySwitcher Switcher() => new(NullLogger<DisplaySwitcher>.Instance, [_screens, _cast, _airPlay]);

    [Fact]
    public async Task SelectAsync_DisconnectsEveryOtherLiveDisplay_BeforeConnecting()
    {
        _screens.ConnectedDeviceId.Returns("screen");
        _cast.ConnectedDeviceId.Returns("tv");

        await Switcher().SelectAsync(_airPlay, Device("speaker"));

        Received.InOrder(() =>
        {
            _screens.DisconnectAsync(Arg.Any<CancellationToken>());
            _cast.DisconnectAsync(Arg.Any<CancellationToken>());
            _airPlay.ConnectAsync("speaker", Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task SelectAsync_LeavesAnIdleDisplayAlone()
    {
        await Switcher().SelectAsync(_airPlay, Device("speaker"));

        await _screens.DidNotReceive().DisconnectAsync(Arg.Any<CancellationToken>());
        await _cast.DidNotReceive().DisconnectAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>The chosen transport decides for itself what moving between its own devices means.</summary>
    [Fact]
    public async Task SelectAsync_LeavesTheChosenTransportConnected_WhileMovingDevices()
    {
        _cast.ConnectedDeviceId.Returns("kitchen-tv");

        await Switcher().SelectAsync(_cast, Device("lounge-tv"));

        await _cast.DidNotReceive().DisconnectAsync(Arg.Any<CancellationToken>());
        await _cast.Received(1).ConnectAsync("lounge-tv", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SelectAsync_ADisconnectThatThrows_DoesNotStopTheRest()
    {
        _screens.ConnectedDeviceId.Returns("screen");
        _screens.DisconnectAsync(Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("screen went away"));
        _cast.ConnectedDeviceId.Returns("tv");

        var connected = await Switcher().SelectAsync(_airPlay, Device("speaker"));

        Assert.True(connected);
        await _cast.Received(1).DisconnectAsync(Arg.Any<CancellationToken>());
        await _airPlay.Received(1).ConnectAsync("speaker", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SelectAsync_TheLiveDevice_TouchesNothing()
    {
        _cast.ConnectedDeviceId.Returns("tv");
        _screens.ConnectedDeviceId.Returns("screen");

        var connected = await Switcher().SelectAsync(_cast, Device("tv"));

        Assert.True(connected);
        await _screens.DidNotReceive().DisconnectAsync(Arg.Any<CancellationToken>());
        await _cast.DidNotReceive().ConnectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SelectAsync_IsFalse_WhenTheTransportRefuses()
    {
        _airPlay.ConnectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        Assert.False(await Switcher().SelectAsync(_airPlay, Device("speaker")));
    }

    [Fact]
    public async Task SelectAsync_IsFalse_WhenTheTransportThrows()
    {
        _airPlay.ConnectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new InvalidOperationException("speaker went away"));

        Assert.False(await Switcher().SelectAsync(_airPlay, Device("speaker")));
    }

    [Fact]
    public async Task TurnOffAsync_DisconnectsEveryLiveDisplay_EvenPastOneThatThrows()
    {
        _screens.ConnectedDeviceId.Returns("screen");
        _screens.DisconnectAsync(Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("screen went away"));
        _cast.ConnectedDeviceId.Returns("tv");

        await Switcher().TurnOffAsync();

        await _screens.Received(1).DisconnectAsync(Arg.Any<CancellationToken>());
        await _cast.Received(1).DisconnectAsync(Arg.Any<CancellationToken>());
        await _airPlay.DidNotReceive().DisconnectAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenLocalScreenAsync_DisconnectsEveryOtherLiveDisplay_BeforeOpeningTheScreen()
    {
        _cast.ConnectedDeviceId.Returns("tv");
        _airPlay.ConnectedDeviceId.Returns("speaker");

        await Switcher().OpenLocalScreenAsync();

        Received.InOrder(() =>
        {
            _cast.DisconnectAsync(Arg.Any<CancellationToken>());
            _airPlay.DisconnectAsync(Arg.Any<CancellationToken>());
            _screens.ConnectAsync("Local Display-device", Arg.Any<CancellationToken>());
        });
        await _cast.DidNotReceive().ConnectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenLocalScreenAsync_ADisconnectThatThrows_StillOpensTheScreen()
    {
        _cast.ConnectedDeviceId.Returns("tv");
        _cast.DisconnectAsync(Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("receiver went away"));

        await Switcher().OpenLocalScreenAsync();

        await _screens.Received(1).ConnectAsync("Local Display-device", Arg.Any<CancellationToken>());
    }

    /// <summary>The provider's own one-screen refusal is what answers a second open, so it is asked.</summary>
    [Fact]
    public async Task OpenLocalScreenAsync_AsksTheScreensAgain_WhenOneIsAlreadyOpen()
    {
        _screens.ConnectedDeviceId.Returns("Local Display-device");

        await Switcher().OpenLocalScreenAsync();

        await _screens.Received(1).ConnectAsync("Local Display-device", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenLocalScreenAsync_AScreenThatThrows_DoesNotPropagate()
    {
        _screens.ConnectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new InvalidOperationException("no window"));

        var open = await Record.ExceptionAsync(() => Switcher().OpenLocalScreenAsync());

        Assert.Null(open);
        await _screens.Received(1).ConnectAsync("Local Display-device", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenLocalScreenAsync_WithNoScreensRegistered_TouchesNothing()
    {
        _cast.ConnectedDeviceId.Returns("tv");
        var switcher = new DisplaySwitcher(NullLogger<DisplaySwitcher>.Instance, [_cast]);

        await switcher.OpenLocalScreenAsync();

        await _cast.DidNotReceive().DisconnectAsync(Arg.Any<CancellationToken>());
        await _cast.DidNotReceive().ConnectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
