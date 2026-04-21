using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging;

namespace KHost.UnitTests.Domain.Services;

public class MediaSearchServiceTests
{

    private readonly ILogger<MediaSearchService> _logger = Substitute.For<ILogger<MediaSearchService>>();
    private readonly IMediaProvider _provider;
    private readonly MediaSearchService _service;

    public MediaSearchServiceTests()
    {
        _provider = Substitute.For<IMediaProvider>();
        _service = new MediaSearchService(_logger, [_provider]);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyList_WhenNoMediaMatch()
    {
        _provider.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(Task.FromResult(new List<MediaSearchEntity>()));

        var results = await _service.SearchAsync("nonexistent");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_AggregatesResultsFromAllProviders()
    {
        var mediaId1 = Guid.NewGuid();
        var mediaId2 = Guid.NewGuid();
        var entities = new List<MediaSearchEntity>
        {
            new MediaSearchEntity { DisplayName = "Artist1 - Media1", Source = "FileSystem", ForeignKey = mediaId1.ToString() },
            new MediaSearchEntity { DisplayName = "Artist2 - Media2", Source = "FileSystem", ForeignKey = mediaId2.ToString() }
        };

        _provider.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(Task.FromResult(entities));

        var results = await _service.SearchAsync("test");

        Assert.Equal(2, results.Count);
        Assert.Equal("Artist1 - Media1", results[0].DisplayName);
        Assert.Equal("Artist2 - Media2", results[1].DisplayName);
        Assert.Equal("FileSystem", results[0].Source);
        Assert.Equal(mediaId1.ToString(), results[0].ForeignKey);
        Assert.Equal("FileSystem", results[1].Source);
        Assert.Equal(mediaId2.ToString(), results[1].ForeignKey);
    }

    [Fact]
    public async Task SearchAsync_CallsProviderWithProvidedPageParameters()
    {
        _provider.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(Task.FromResult(new List<MediaSearchEntity>()));

        await _service.SearchAsync("test", pageNumber: 2, pageSize: 50);

        await _provider.Received(1).SearchAsync("test", pageNumber: 2, pageSize: 50);
    }
}
