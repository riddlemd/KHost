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

    [Fact]
    public void Title_CountsTheScreens_WhenNoDeviceIsShowing()
    {
        _button._screenCount = 2;

        Assert.Equal("Screens: 2 connected", _button.Title);
    }

    [Fact]
    public void Title_NamesTheDevice_SinceTheCountLeftTheButton()
    {
        _button._screenCount = 0;
        Connected("Office Room TV");

        Assert.Equal("Screens: none connected, showing on Office Room TV", _button.Title);
    }

    [Fact]
    public void Title_ReportsBoth_WhenScreensAndADeviceAreLive()
    {
        _button._screenCount = 1;
        Connected("Office Room TV");

        Assert.Equal("Screens: 1 connected, showing on Office Room TV", _button.Title);
    }

    [Fact]
    public void Title_CountsScreensOnly_WhenNoPluginSuppliesAProvider()
    {
        // [Inject] resolves by type and ignores the nullable annotation, so the component takes the
        // enumerable: with no plugin installed it is empty, and asking a null provider must not throw.
        typeof(ScreensButton)
            .GetProperty("DisplayProviders", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(_button, Array.Empty<IDisplayProvider>());
        _button._screenCount = 2;

        Assert.False(_button.IsSendingToDevice);
        Assert.Equal("Screens: 2 connected", _button.Title);
    }

    private void Connected(string name)
    {
        _display.ConnectedDeviceId.Returns("device-1");
        _display.Devices.Returns([new DisplayDevice { Id = "device-1", Name = name, IsConnected = true }]);
    }
}
