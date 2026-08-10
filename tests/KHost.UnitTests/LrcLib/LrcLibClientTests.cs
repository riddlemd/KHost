using System.Globalization;
using System.Net;
using System.Text;
using KHost.LrcLib;
using KHost.LrcLib.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.LrcLib;

public class LrcLibClientTests
{
    private const string BaseAddress = "https://lrclib.example/";

    private static (LrcLibClient Client, StubHandler Handler) MakeClient(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string body = "null")
    {
        var handler = new StubHandler(statusCode, body);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(BaseAddress) };

        return (new LrcLibClient(httpClient, NullLogger<LrcLibClient>.Instance), handler);
    }

    private static string RecordJson(long id = 1, string track = "Song", string artist = "Band") => $$"""
        {
          "id": {{id}},
          "trackName": "{{track}}",
          "artistName": "{{artist}}",
          "albumName": null,
          "duration": 210.5,
          "instrumental": false,
          "plainLyrics": "la la la",
          "syncedLyrics": null
        }
        """;

    [Theory]
    [InlineData("", "Band")]
    [InlineData("   ", "Band")]
    [InlineData("Song", "")]
    [InlineData("Song", "  ")]
    public async Task GetAsync_Throws_WhenTrackOrArtistIsBlank(string track, string artist)
    {
        var (client, handler) = MakeClient();

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetAsync(new GetLyricsRequest(track, artist)));
        Assert.Null(handler.LastRequestUri);
    }

    [Theory]
    [InlineData("", "Band")]
    [InlineData("Song", "")]
    public async Task GetCachedAsync_Throws_WhenTrackOrArtistIsBlank(string track, string artist)
    {
        var (client, handler) = MakeClient();

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetCachedAsync(new GetLyricsRequest(track, artist)));
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task GetAsync_TargetsTheGetEndpoint()
    {
        var (client, handler) = MakeClient(body: RecordJson());

        await client.GetAsync(new GetLyricsRequest("Song", "Band"));

        Assert.StartsWith($"{BaseAddress}api/get?", handler.LastRequestUri);
    }

    [Fact]
    public async Task GetCachedAsync_TargetsTheGetCachedEndpoint()
    {
        var (client, handler) = MakeClient(body: RecordJson());

        await client.GetCachedAsync(new GetLyricsRequest("Song", "Band"));

        Assert.StartsWith($"{BaseAddress}api/get-cached?", handler.LastRequestUri);
    }

    [Fact]
    public async Task GetByIdAsync_TargetsTheIdEndpoint()
    {
        var (client, handler) = MakeClient(body: RecordJson(id: 4242));

        await client.GetByIdAsync(4242);

        Assert.Equal($"{BaseAddress}api/get/4242", handler.LastRequestUri);
    }

    [Fact]
    public async Task GetAsync_EscapesQueryValues()
    {
        var (client, handler) = MakeClient(body: RecordJson());

        await client.GetAsync(new GetLyricsRequest("Rock & Roll", "AC/DC"));

        Assert.Contains("track_name=Rock%20%26%20Roll", handler.LastRequestUri);
        Assert.Contains("artist_name=AC%2FDC", handler.LastRequestUri);
    }

    [Fact]
    public async Task GetAsync_OmitsAlbumAndDuration_WhenNotSupplied()
    {
        var (client, handler) = MakeClient(body: RecordJson());

        await client.GetAsync(new GetLyricsRequest("Song", "Band"));

        Assert.DoesNotContain("album_name", handler.LastRequestUri);
        Assert.DoesNotContain("duration", handler.LastRequestUri);
    }

    [Fact]
    public async Task GetAsync_FormatsDurationInvariantly()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var (client, handler) = MakeClient(body: RecordJson());

            await client.GetAsync(new GetLyricsRequest("Song", "Band", Duration: 210.5));

            // A comma decimal separator would make the API reject the request.
            Assert.Contains("duration=210.5", handler.LastRequestUri);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_OnNotFound()
    {
        var (client, _) = MakeClient(HttpStatusCode.NotFound, "");

        var result = await client.GetAsync(new GetLyricsRequest("Song", "Band"));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_Throws_OnServerError()
    {
        var (client, _) = MakeClient(HttpStatusCode.InternalServerError, "");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new GetLyricsRequest("Song", "Band")));
    }

    [Fact]
    public async Task GetAsync_DeserializesTheRecord()
    {
        var (client, _) = MakeClient(body: RecordJson(id: 7, track: "Song", artist: "Band"));

        var result = await client.GetAsync(new GetLyricsRequest("Song", "Band"));

        Assert.NotNull(result);
        Assert.Equal(7, result.Id);
        Assert.Equal("la la la", result.PlainLyrics);
        Assert.False(result.Instrumental);
    }

    [Fact]
    public async Task SearchAsync_Throws_WhenNeitherQueryNorTrackNameSupplied()
    {
        var (client, handler) = MakeClient();

        await Assert.ThrowsAsync<ArgumentException>(() => client.SearchAsync(new SearchLyricsRequest(ArtistName: "Band")));
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task SearchAsync_AcceptsTrackNameWithoutQuery()
    {
        var (client, handler) = MakeClient(body: "[]");

        await client.SearchAsync(new SearchLyricsRequest(TrackName: "Song"));

        Assert.Contains("track_name=Song", handler.LastRequestUri);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmpty_WhenBodyIsNull()
    {
        var (client, _) = MakeClient(body: "null");

        var results = await client.SearchAsync(new SearchLyricsRequest(Query: "song"));

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ReturnsAllRecords()
    {
        var (client, _) = MakeClient(body: $"[{RecordJson(1)},{RecordJson(2)}]");

        var results = await client.SearchAsync(new SearchLyricsRequest(Query: "song"));

        Assert.Equal([1L, 2L], results.Select(r => r.Id));
    }

    [Fact]
    public async Task SearchAsync_Throws_OnServerError()
    {
        var (client, _) = MakeClient(HttpStatusCode.BadGateway, "");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchAsync(new SearchLyricsRequest(Query: "song")));
    }

    [Fact]
    public async Task SearchAsync_DoesNotTreatNotFoundAsEmpty()
    {
        var (client, _) = MakeClient(HttpStatusCode.NotFound, "");

        // Unlike the get endpoints, search has no 404-means-no-match contract.
        await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchAsync(new SearchLyricsRequest(Query: "song")));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public string? LastRequestUri { get; private set; }

        public StubHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // AbsoluteUri preserves percent-encoding; ToString() unescapes it.
            LastRequestUri = request.RequestUri?.AbsoluteUri;

            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
