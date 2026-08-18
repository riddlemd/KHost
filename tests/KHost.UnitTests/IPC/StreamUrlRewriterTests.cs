using KHost.IPC.SignalR;

namespace KHost.UnitTests.IPC;

public class StreamUrlRewriterTests
{
    private const string LoopbackUrl = "http://localhost:5251/media/abc/stream.m3u8";

    [Fact]
    public void ForScreen_HandsARemoteScreenTheAddressItReachedUsOn()
    {
        var url = StreamUrlRewriter.ForScreen(LoopbackUrl, "192.168.0.99");

        Assert.Equal("http://192.168.0.99:5251/media/abc/stream.m3u8", url);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    public void ForScreen_LeavesAScreenOnThisMachineAlone(string hostAddress)
        => Assert.Equal(LoopbackUrl, StreamUrlRewriter.ForScreen(LoopbackUrl, hostAddress));

    [Fact]
    public void ForScreen_LeavesAnAlreadyRoutableUrlAlone()
    {
        const string routable = "http://10.0.0.5:5251/media/abc/stream.m3u8";

        Assert.Equal(routable, StreamUrlRewriter.ForScreen(routable, "192.168.0.99"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-address")]
    public void ForScreen_KeepsTheUrl_WhenTheHostAddressIsUnusable(string? hostAddress)
        => Assert.Equal(LoopbackUrl, StreamUrlRewriter.ForScreen(LoopbackUrl, hostAddress));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ForScreen_PassesAMissingUrlThrough(string? streamUrl)
        => Assert.Equal(streamUrl, StreamUrlRewriter.ForScreen(streamUrl, "192.168.0.99"));

    [Fact]
    public void ForScreen_KeepsThePortAndPath()
    {
        var url = StreamUrlRewriter.ForScreen("http://127.0.0.1:5251/media/s/seg_00007.ts", "192.168.0.99");

        Assert.Equal("http://192.168.0.99:5251/media/s/seg_00007.ts", url);
    }
}
