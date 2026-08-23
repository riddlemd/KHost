using System.Net;
using KHost.UserInterface.Http;
using Microsoft.AspNetCore.Http;

namespace KHost.UnitTests.UserInterface.Http;

public class LanAccessPolicyTests
{
    private static readonly IPAddress Lan = IPAddress.Parse("192.168.0.42");
    private static readonly IPAddress Loopback = IPAddress.Parse("127.0.0.1");
    private static readonly HostString LoopbackHost = new("localhost:5251");
    private static readonly HostString ForeignHost = new("attacker.com");

    [Theory]
    [InlineData("/media/abc123/stream.m3u8")]
    [InlineData("/media/abc123/seg_00000.ts")]
    [InlineData("/ipc/screen")]
    [InlineData("/ipc/screen/negotiate")]
    public void IsAllowed_LetsALanConsumerReachTheStreamAndTheHub(string path)
        => Assert.True(LanAccessPolicy.IsAllowed(Lan, ForeignHost, new PathString(path)));

    [Theory]
    [InlineData("/")]
    [InlineData("/venue-settings")]
    [InlineData("/_blazor")]
    [InlineData("/app.css")]
    [InlineData("/not-found")]
    public void IsAllowed_KeepsEverythingElseOffTheLan(string path)
    {
        // The UI has no auth of its own, so anyone on the venue wifi could drive the queue.
        Assert.False(LanAccessPolicy.IsAllowed(Lan, ForeignHost, new PathString(path)));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    public void IsAllowed_GivesThisMachineTheWholeApp(string address)
    {
        // ::ffff:127.0.0.1 is how a v4 client arrives on a dual-stack socket; unwrapped it is loopback.
        Assert.True(LanAccessPolicy.IsAllowed(IPAddress.Parse(address), LoopbackHost, new PathString("/venue-settings")));
    }

    [Fact]
    public void IsAllowed_TreatsAnInProcessRequestAsLocal()
        => Assert.True(LanAccessPolicy.IsAllowed(null, default, new PathString("/venue-settings")));

    [Fact]
    public void IsAllowed_DoesNotMatchAPathThatMerelyStartsWithAnAllowedName()
    {
        // StartsWithSegments, not StartsWith: /mediabrowser is not /media.
        Assert.False(LanAccessPolicy.IsAllowed(Lan, ForeignHost, new PathString("/mediabrowser")));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("localhost:5251")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:5251")]
    [InlineData("[::1]")]
    [InlineData("[::1]:5251")]
    public void IsAllowed_TrustsLoopbackOnlyWhenHostIsAlsoLoopback(string host)
        => Assert.True(LanAccessPolicy.IsAllowed(Loopback, new HostString(host), new PathString("/venue-settings")));

    [Fact]
    public void IsAllowed_DeniesALoopbackSourceWhoseHostHeaderIsForeign()
    {
        // DNS rebinding: a page the operator is browsing repoints its hostname at 127.0.0.1, so
        // RemoteIpAddress is loopback but Host still names the attacker's domain.
        Assert.False(LanAccessPolicy.IsAllowed(Loopback, ForeignHost, new PathString("/venue-settings")));
    }
}
