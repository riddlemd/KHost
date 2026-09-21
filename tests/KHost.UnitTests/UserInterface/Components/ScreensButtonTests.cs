using System.Reflection;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components;
using NSubstitute;

namespace KHost.UnitTests.UserInterface.Components;

public class ScreensButtonTests
{
    private readonly IDisplayProvider _display = Substitute.For<IDisplayProvider>();
    private readonly ScreensButton _button = new();

    public ScreensButtonTests()
        => typeof(ScreensButton)
            .GetProperty("DisplayProviders", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(_button, new[] { _display });

    [Fact]
    public void IsActive_IsFalse_WithNothingConnected()
    {
        // NSubstitute hands back string.Empty for an unstubbed string, not null.
        Assert.False(_button.IsActive);
    }

    [Fact]
    public void IsActive_IsTrue_WhenAScreenIsConnected()
    {
        _button._screenCount = 1;

        Assert.True(_button.IsActive);
    }

    [Fact]
    public void IsActive_IsTrue_WhenOnlyADeviceIsShowing()
    {
        // A provider's device is not a screen, but the room is still watching something.
        Connected("Office Room TV");

        Assert.True(_button.IsActive);
    }

    // The button does one thing per press, so the tooltip says which rather than reporting a count
    // the host can no longer act on from here.
    [Fact]
    public void Title_OffersToOpenOne_WithNothingConnected()
        => Assert.Equal("Open a screen", _button.Title);

    [Fact]
    public void Title_OffersToCloseIt_WhenOneIsConnected()
    {
        _button._screenCount = 1;

        Assert.Equal("Close the screen", _button.Title);
    }

    /// <summary>Closing takes all of them, so the tooltip must not say "the screen" and take two.</summary>
    [Fact]
    public void Title_SaysHowManyItWillClose_WhenSeveralAreConnected()
    {
        _button._screenCount = 2;

        Assert.Equal("Close 2 screens", _button.Title);
    }

    /// <summary>A device is not a screen: the press still opens one, and the device is context.</summary>
    [Fact]
    public void Title_StillOffersToOpenOne_WhenOnlyADeviceIsShowing()
    {
        Connected("Office Room TV");

        Assert.Equal("Open a screen (showing on Office Room TV)", _button.Title);
    }

    /// <summary>With a screen up, the press closes it whatever a device is doing.</summary>
    [Fact]
    public void Title_OffersToClose_WhenAScreenAndADeviceAreBothLive()
    {
        _button._screenCount = 1;
        Connected("Office Room TV");

        Assert.Equal("Close the screen", _button.Title);
    }

    [Fact]
    public void Title_SaysNothingOfDevices_WhenNoPluginSuppliesAProvider()
    {
        // [Inject] resolves by type and ignores the nullable annotation, so the component takes the
        // enumerable: with no plugin installed it is empty, and asking a null provider must not throw.
        typeof(ScreensButton)
            .GetProperty("DisplayProviders", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(_button, Array.Empty<IDisplayProvider>());

        Assert.False(_button.IsSendingToDevice);
        Assert.Equal("Open a screen", _button.Title);
    }

    /// <summary>A launched screen takes seconds to connect, and the button must not read as an
    /// invitation to press it again during them.</summary>
    [Fact]
    public void Title_SaysItIsOpening_WhileALaunchIsInFlight()
    {
        _button._isBusy = true;

        Assert.Equal("Opening a screen…", _button.Title);
    }

    private void Connected(string name)
    {
        _display.ConnectedDeviceId.Returns("device-1");
        _display.Devices.Returns([new DisplayDevice { Id = "device-1", Name = name, IsConnected = true }]);
    }
}
