using System.Net;
using System.Net.Http.Headers;
using KHost.Domain.Services.Displays.LocalScreen;
using KHost.Domain.Services.Visualisations;
using KHost.UserInterface.Startup;

namespace KHost.UnitTests.UserInterface.Startup;

public sealed class NativeShellRequestsTests
{
    private static readonly Dictionary<string, string> NoHeaders = [];

    [Theory]
    [InlineData("/media/image/9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d")]
    [InlineData("/media/session/song.m3u8")]
    [InlineData(VisualiserPresetService.RoutePrefix + "classic")]
    [InlineData(FfmpegSongLevelsService.RoutePrefix + "token")]
    [InlineData(VideoBackdropService.RoutePrefix + "token")]
    [InlineData("/plugins/88f42c92-f15a-443c-afa0-0983f05fdbb4/icon.png")]
    [InlineData("/css/themes/custom/mine.css")]
    public void IsHostEndpoint_TheHostsOwnRoutes_IsTrue(string path)
        => Assert.True(NativeShellRequests.IsHostEndpoint(path));

    [Theory]
    [InlineData("/")]
    [InlineData("/settings")]
    [InlineData("/css/app.css")]
    [InlineData("/css/themes/outrun.css")]
    [InlineData("/js/shortcuts.js")]
    [InlineData("/_framework/blazor.webview.js")]
    [InlineData("/mediaplayer")]
    public void IsHostEndpoint_TheConsolesOwnFilesAndRoutes_IsFalse(string path)
        => Assert.False(NativeShellRequests.IsHostEndpoint(path));

    [Fact]
    public void ShellScript_Debug_FlagsTheNativeSurfaceAndKeepsTheTools()
    {
        var script = NativeShellRequests.ShellScript(lockDown: false);

        Assert.Contains("khSurface = 'native'", script);
        Assert.Contains("khDevtools = 'allow'", script);
        Assert.DoesNotContain("shell-lockdown.js", script);
    }

    [Fact]
    public void ShellScript_Release_BlocksTheToolsAndLocksTheWindowDown()
    {
        var script = NativeShellRequests.ShellScript(lockDown: true);

        Assert.Contains("khDevtools = 'block'", script);
        Assert.Contains("js/shell-lockdown.js", script);
    }

    [Fact]
    public async Task HandleAsync_TheShellScript_IsAnsweredWithoutTheHost()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("not for the host"));
        using var host = Host(handler);

        var response = await Handle("app://localhost" + NativeShellRequests.ShellScriptPath, host, lockDown: true);

        Assert.NotNull(response);
        Assert.StartsWith("text/javascript", response.ContentType);
        Assert.Equal(NativeShellRequests.ShellScript(lockDown: true), await new StreamReader(response.Body!).ReadToEndAsync());
    }

    [Fact]
    public async Task HandleAsync_AConsoleFile_IsLeftToTheStaticFiles()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("not for the host"));
        using var host = Host(handler);

        Assert.Null(await Handle("app://localhost/css/app.css?v=1", host));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task HandleAsync_AHostEndpoint_GoesToTheHostWithItsQueryAndHeaders()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } },
        });
        using var host = Host(handler);

        var response = await Handle(
            "app://localhost/media/image/9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d?size=64", host,
            headers: new Dictionary<string, string> { ["Accept"] = "image/*", ["Host"] = "localhost" });

        var sent = Assert.Single(handler.Requests);
        Assert.Equal("http://127.0.0.1:5251/media/image/9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d?size=64", sent.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal("image/*", sent.Headers.Accept.ToString());
        Assert.Null(sent.Headers.Host);
        Assert.NotNull(response);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("image/png", response.ContentType);
        Assert.Equal(3, response.Length);
        Assert.Equal(new byte[] { 1, 2, 3 }, ReadAll(response.Body!));
    }

    /// <summary>A song seeks by range: the host's slice and its headers must reach Chromium as they are.</summary>
    [Fact]
    public async Task HandleAsync_ARangeRequest_PassesTheRangeAndThePartialResponseThrough()
    {
        var handler = new RecordingHandler(_ =>
        {
            var reply = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent([7, 8]) };
            reply.Content.Headers.ContentRange = new ContentRangeHeaderValue(10, 11, 100);
            reply.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            reply.Headers.AcceptRanges.Add("bytes");
            return reply;
        });
        using var host = Host(handler);

        var response = await Handle(
            "app://localhost/media/session/song.mp4", host,
            headers: new Dictionary<string, string> { ["Range"] = "bytes=10-11" });

        Assert.Equal("bytes=10-11", Assert.Single(handler.Requests).Headers.Range!.ToString());
        Assert.NotNull(response);
        Assert.Equal(206, response.StatusCode);
        Assert.Equal("bytes 10-11/100", response.Headers["Content-Range"]);
        Assert.Equal("bytes", response.Headers["Accept-Ranges"]);
        Assert.False(response.Headers.ContainsKey("Content-Type"));
    }

    [Fact]
    public async Task HandleAsync_APostWithABody_SendsTheBody()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var host = Host(handler);

        var response = await Handle(
            "app://localhost/css/themes/custom/mine.css", host, method: "POST", body: [42],
            headers: new Dictionary<string, string> { ["Content-Type"] = "text/css" });

        Assert.Equal(HttpMethod.Post, Assert.Single(handler.Requests).Method);
        Assert.Equal(new byte[] { 42 }, handler.Bodies.Single());
        Assert.Equal("text/css", handler.Requests[0].Content!.Headers.ContentType!.ToString());
        Assert.Equal(204, response!.StatusCode);
    }

    [Fact]
    public async Task HandleAsync_TheHostsError_IsPassedOnRatherThanTheHostPage()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { ReasonPhrase = "Not Found" });
        using var host = Host(handler);

        var response = await Handle("app://localhost/plugins/missing/icon.png", host);

        Assert.Equal(404, response!.StatusCode);
        Assert.Equal("Not Found", response.StatusText);
    }

    private static Task<Oriel.ProtocolResponse?> Handle(
        string url, HttpClient host, bool lockDown = false, string method = "GET", byte[]? body = null,
        Dictionary<string, string>? headers = null)
        => NativeShellRequests.HandleAsync(url, method, headers ?? NoHeaders, body, host, lockDown, CancellationToken.None);

    private static HttpClient Host(HttpMessageHandler handler)
        => new(handler) { BaseAddress = new Uri("http://127.0.0.1:5251/") };

    private static byte[] ReadAll(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<byte[]> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null) Bodies.Add(await request.Content.ReadAsByteArrayAsync(cancellationToken));
            return reply(request);
        }
    }
}
