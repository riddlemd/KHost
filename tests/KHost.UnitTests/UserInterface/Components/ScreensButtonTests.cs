using System.Reflection;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components;
using NSubstitute;

namespace KHost.UnitTests.UserInterface.Components;

public class ScreensButtonTests
{
    private readonly ICastService _cast = Substitute.For<ICastService>();
    private readonly ScreensButton _button = new();

    public ScreensButtonTests()
        => typeof(ScreensButton)
            .GetProperty("Cast", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(_button, _cast);

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
    public void IsActive_IsTrue_WhenOnlyCasting()
    {
        // A receiver is not a screen, but the room is still watching something.
        Connected("Office Room TV");

        Assert.True(_button.IsActive);
    }

    [Fact]
    public void Title_CountsTheScreens_WhenNotCasting()
    {
        _button._screenCount = 2;

        Assert.Equal("Screens — 2 connected", _button.Title);
    }

    [Fact]
    public void Title_NamesTheReceiver_SinceTheCountLeftTheButton()
    {
        _button._screenCount = 0;
        Connected("Office Room TV");

        Assert.Equal("Screens — none connected, casting to Office Room TV", _button.Title);
    }

    [Fact]
    public void Title_ReportsBoth_WhenScreensAndACastAreLive()
    {
        _button._screenCount = 1;
        Connected("Office Room TV");

        Assert.Equal("Screens — 1 connected, casting to Office Room TV", _button.Title);
    }

    private void Connected(string name)
    {
        _cast.ConnectedDeviceId.Returns("device-1");
        _cast.Devices.Returns([new CastDevice { Id = "device-1", Name = name, IsConnected = true }]);
    }
}
