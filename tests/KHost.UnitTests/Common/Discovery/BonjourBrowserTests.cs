using KHost.Common.Discovery;

namespace KHost.UnitTests.Common.Discovery;

/// <summary>The macOS browse path any network-device plugin needs: a .NET socket cannot multicast
/// there — the send fails with EHOSTUNREACH while unicast to the same host succeeds — so discovery
/// goes through the system daemon. These cover the decisions the interop makes for itself.</summary>
public class BonjourBrowserTests
{
    private const uint Add = 0x2;
    private const uint MoreComing = 0x1;

    [Fact]
    public void IsArrival_IsTrue_ForAServiceAppearing()
        => Assert.True(BonjourBrowser.IsArrival(Add, 0));

    /// <summary>A departure arrives through the same callback with the flag clear; taking it for an
    /// arrival lists a device that has just left the network.</summary>
    [Fact]
    public void IsArrival_IsFalse_ForAServiceGoingAway()
        => Assert.False(BonjourBrowser.IsArrival(0, 0));

    [Fact]
    public void IsArrival_IsFalse_WhenTheDaemonReportsAnError()
        => Assert.False(BonjourBrowser.IsArrival(Add, -65540));

    /// <summary>Other flags ride alongside and must not change the answer.</summary>
    [Fact]
    public void IsArrival_IgnoresUnrelatedFlags()
    {
        Assert.True(BonjourBrowser.IsArrival(Add | MoreComing, 0));
        Assert.False(BonjourBrowser.IsArrival(MoreComing, 0));
    }

    /// <summary>Only macOS needs this path; everywhere else keeps the managed one.</summary>
    [Fact]
    public void IsSupported_TracksTheOperatingSystem()
        => Assert.Equal(OperatingSystem.IsMacOS(), BonjourBrowser.IsSupported);
}
