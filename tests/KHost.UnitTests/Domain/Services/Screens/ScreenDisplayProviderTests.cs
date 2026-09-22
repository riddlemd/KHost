using KHost.Abstractions.Messaging;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Domain.Services.Screens;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KHost.UnitTests.Domain.Services.Screens;

public class ScreenDisplayProviderTests
{
    private readonly IScreenServer _screenServer = Substitute.For<IScreenServer>();
    private readonly IMessageBroker _broker = Substitute.For<IMessageBroker>();
    private readonly ScreenDisplayProvider _provider;

    public ScreenDisplayProviderTests()
        => _provider = new ScreenDisplayProvider(
            NullLogger<ScreenDisplayProvider>.Instance, _screenServer, [], _broker);

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
}
