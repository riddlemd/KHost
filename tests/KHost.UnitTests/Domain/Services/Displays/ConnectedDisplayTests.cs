using KHost.Abstractions.Services;
using KHost.Domain.Services.Displays;
using NSubstitute;

namespace KHost.UnitTests.Domain.Services.Displays;

public class ConnectedDisplayTests
{
    private static IDisplayProvider Provider(string? connectedId, params DisplayDevice[] devices)
    {
        var provider = Substitute.For<IDisplayProvider>();
        provider.ConnectedDeviceId.Returns(connectedId);
        provider.Devices.Returns(devices);

        return provider;
    }

    private static DisplayDevice Device(string id, bool isConnected = false)
        => new() { Id = id, Name = id, IsConnected = isConnected };

    [Fact]
    public void Find_NothingConnected_IsNull()
        => Assert.Null(ConnectedDisplay.Find([Provider(null), Provider("")]));

    /// <summary>The screens and a plugin transport sit side by side; only one holds the song.</summary>
    [Fact]
    public void Find_AmongSeveralProviders_ReturnsTheConnectedOne()
    {
        var idle = Provider(null, Device("tv"));
        var live = Provider("Screen 1", Device("Screen 1", isConnected: true));

        var found = ConnectedDisplay.Find([idle, live]);

        Assert.NotNull(found);
        Assert.Same(live, found.Value.Provider);
        Assert.Equal("Screen 1", found.Value.Device?.Id);
    }

    [Fact]
    public void Find_DeviceListedUnderAnotherId_FallsBackToTheConnectedRow()
    {
        var live = Provider("session-7", Device("other"), Device("tv", isConnected: true));

        Assert.Equal("tv", ConnectedDisplay.Find([live])?.Device?.Id);
    }

    /// <summary>Connected before it lists anything: still the display, with its device unknown.</summary>
    [Fact]
    public void Find_ConnectedButUnlisted_ReturnsTheProviderWithNoDevice()
    {
        var live = Provider("tv");

        var found = ConnectedDisplay.Find([live]);

        Assert.NotNull(found);
        Assert.Same(live, found.Value.Provider);
        Assert.Null(found.Value.Device);
    }
}
