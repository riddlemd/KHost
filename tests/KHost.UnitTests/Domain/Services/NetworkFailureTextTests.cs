using System.Net;
using System.Net.Sockets;
using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

public class NetworkFailureTextTests
{
    private const string Unreachable = "Could not reach LRCLIB. Check this computer is online, then try again.";

    public static TheoryData<Exception> NetworkFailures => new()
    {
        new HttpRequestException("Permission denied (raw.githubusercontent.com:443)"),
        new SocketException((int)SocketError.HostUnreachable),
        new IOException("Unable to read data", new SocketException((int)SocketError.ConnectionReset)),
        new IOException("Unable to read data", new HttpRequestException("reset")),
        new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout", new TimeoutException()),
    };

    [Theory]
    [MemberData(nameof(NetworkFailures))]
    public void Describe_NetworkFailure_SaysTheThingCouldNotBeReached(Exception exception)
    {
        Assert.Equal(Unreachable, NetworkFailureText.Describe("LRCLIB", exception));
    }

    [Fact]
    public void Describe_ServerAnsweredWithAStatus_IsNotCalledUnreachable()
    {
        var exception = new HttpRequestException("Response status code does not indicate success: 404 (Not Found).", null, HttpStatusCode.NotFound);

        Assert.Equal("LRCLIB failed: Response status code does not indicate success: 404 (Not Found).",
            NetworkFailureText.Describe("LRCLIB", exception));
    }

    [Fact]
    public void Describe_IOExceptionWithNoNetworkCause_ReportsItsMessage()
    {
        Assert.Equal("LRCLIB failed: Disk full", NetworkFailureText.Describe("LRCLIB", new IOException("Disk full")));
    }

    [Fact]
    public void Describe_CancellationTheCallerAskedFor_IsNotCalledATimeout()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Equal("LRCLIB failed: A task was canceled.",
            NetworkFailureText.Describe("LRCLIB", new TaskCanceledException(), cts.Token));
    }

    [Fact]
    public void Describe_OtherFailure_StartsTheSentenceWithACapital()
    {
        Assert.Equal("The plugin catalog failed: Catalog is empty.",
            NetworkFailureText.Describe("the plugin catalog", new InvalidOperationException("Catalog is empty.")));
    }
}
