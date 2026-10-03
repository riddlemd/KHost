using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.LrcLib;
using KHost.LrcLib.Models;
using Microsoft.Extensions.Logging.Abstractions;
using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

public class LyricsServiceTests
{
    private const string Unreachable = "Could not reach LRCLIB. Check this computer is online, then try again.";

    private readonly ILrcLibClient _client = Substitute.For<ILrcLibClient>();
    private readonly IFlashService _flash = Substitute.For<IFlashService>();
    private readonly LyricsService _service;

    public LyricsServiceTests()
    {
        _service = new LyricsService(NullLogger<LyricsService>.Instance, _client, _flash);
    }

    [Fact]
    public async Task SearchAsync_ReturnsNull_ForNullQuery()
    {
        var result = await _service.SearchAsync(null!);

        Assert.Null(result);
    }

    [Fact]
    public async Task SearchAsync_ReturnsNull_ForWhitespaceQuery()
    {
        var result = await _service.SearchAsync("   ");

        Assert.Null(result);
    }

    [Fact]
    public async Task SearchAsync_ReturnsNull_WhenClientReturnsEmptyList()
    {
        _client.SearchAsync(Arg.Any<SearchLyricsRequest>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<LyricsRecord>)new List<LyricsRecord>());

        var result = await _service.SearchAsync("hello");

        Assert.Null(result);
    }

    [Fact]
    public async Task SearchAsync_ReturnsMappedLyrics_ForValidResult()
    {
        var record = new LyricsRecord(1, "My Song", "My Artist", null, null, false, "verse one", null);
        _client.SearchAsync(Arg.Any<SearchLyricsRequest>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<LyricsRecord>)new List<LyricsRecord> { record });

        var result = await _service.SearchAsync("my song");

        Assert.NotNull(result);
        Assert.Contains("My Song", result.Name);
        Assert.Contains("My Artist", result.Name);
        Assert.Equal("verse one", result.Text);
    }

    [Fact]
    public async Task SearchAsync_MapsNullPlainLyricsToEmptyString()
    {
        var record = new LyricsRecord(1, "Instrumental", "Artist", null, null, true, null, null);
        _client.SearchAsync(Arg.Any<SearchLyricsRequest>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<LyricsRecord>)new List<LyricsRecord> { record });

        var result = await _service.SearchAsync("instrumental");

        Assert.NotNull(result);
        Assert.Equal("", result.Text);
    }

    [Fact]
    public async Task SearchAsync_ReturnsNull_OnHttpRequestException()
    {
        _client.SearchAsync(Arg.Any<SearchLyricsRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<LyricsRecord>>(new HttpRequestException()));

        var result = await _service.SearchAsync("hello");

        Assert.Null(result);
        _flash.Received(1).Show(Unreachable, FlashType.Warning);
    }

    [Fact]
    public async Task SearchAsync_HttpClientTimesOut_ReturnsNullAndFlashes()
    {
        _client.SearchAsync(Arg.Any<SearchLyricsRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<LyricsRecord>>(
                new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout", new TimeoutException())));

        var result = await _service.SearchAsync("hello");

        Assert.Null(result);
        _flash.Received(1).Show(Unreachable, FlashType.Warning);
    }

    [Fact]
    public async Task SearchAsync_AnswerCannotBeRead_ReturnsNullAndFlashesWhy()
    {
        _client.SearchAsync(Arg.Any<SearchLyricsRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<LyricsRecord>>(new System.Text.Json.JsonException("Bad JSON")));

        var result = await _service.SearchAsync("hello");

        Assert.Null(result);
        _flash.Received(1).Show("LRCLIB failed: Bad JSON", FlashType.Warning);
    }

    [Fact]
    public async Task SearchAsync_CallerCancels_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _client.SearchAsync(Arg.Any<SearchLyricsRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<LyricsRecord>>(new TaskCanceledException()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.SearchAsync("hello", cts.Token));
        _flash.DidNotReceive().Show(Arg.Any<string>(), Arg.Any<FlashType>());
    }
}
