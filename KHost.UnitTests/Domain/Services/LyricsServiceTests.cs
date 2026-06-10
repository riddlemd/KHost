using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.LrcLib;
using KHost.LrcLib.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

public class LyricsServiceTests
{
    private readonly ILrcLibClient _client = Substitute.For<ILrcLibClient>();
    private readonly LyricsService _service;

    public LyricsServiceTests()
    {
        _service = new LyricsService(NullLogger<LyricsService>.Instance, _client);
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
    }
}
