using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;
using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

public class MediaSearchServiceTests
{

    private readonly ILogger<MediaSearchService> _logger = Substitute.For<ILogger<MediaSearchService>>();
    private readonly IMediaProvider _provider;
    private readonly MediaSearchService _service;

    public MediaSearchServiceTests()
    {
        _provider = Substitute.For<IMediaProvider>();
        _provider.SourceName.Returns("FileSystem");
        _provider.DisplayName.Returns("File System");
        var analytics = Substitute.For<IAnalyticsService>();
        analytics.StartActivity(Arg.Any<string>()).Returns(Substitute.For<IAnalyticsActivity>());
        _service = new MediaSearchService(_logger, [_provider], analytics);
    }

    [Fact]
    public void Providers_ExposesEveryRegisteredProvider()
    {
        Assert.Single(_service.Providers);
        Assert.Equal("FileSystem", _service.Providers[0].SourceName);
    }

    [Fact]
    public async Task SearchAsync_BySource_AsksOnlyTheMatchingProvider()
    {
        var other = Substitute.For<IMediaProvider>();
        other.SourceName.Returns("YouTube");
        var analytics = Substitute.For<IAnalyticsService>();
        analytics.StartActivity(Arg.Any<string>()).Returns(Substitute.For<IAnalyticsActivity>());
        var service = new MediaSearchService(_logger, [_provider, other], analytics);

        _provider.SearchAsync("song", 0, 0).Returns([Entity("FileSystem", "song")]);

        var results = await service.SearchAsync("song", "FileSystem");

        Assert.Single(results);
        Assert.Equal("FileSystem", results[0].Source);
        await other.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public async Task SearchAsync_BySource_FindsNothing_ForAnUnknownSource()
    {
        var results = await _service.SearchAsync("song", "NoSuchSource");

        Assert.Empty(results);
        await _provider.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
    }

    private static MediaSearchEntity Entity(string source, string name) => new()
    {
        Source = source,
        SourceDisplayName = source,
        ForeignKey = Guid.NewGuid().ToString(),
        Title = name,
    };

    [Fact]
    public void GetMediaProviderDisplayName_ReturnsDisplayName_WhenProviderFound()
    {
        var result = _service.GetMediaProviderDisplayName("FileSystem");

        Assert.Equal("File System", result);
    }

    [Fact]
    public void GetMediaProviderDisplayName_ReturnsUnknownSource_WhenProviderNotFound()
    {
        var result = _service.GetMediaProviderDisplayName("YouTube");

        Assert.Equal("Unknown Source", result);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyList_WhenNoProviders()
    {
        var analytics = Substitute.For<IAnalyticsService>();
        analytics.StartActivity(Arg.Any<string>()).Returns(Substitute.For<IAnalyticsActivity>());
        var emptyService = new MediaSearchService(_logger, [], analytics);

        var results = await emptyService.SearchAllAsync("test");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ContinuesAfterProviderException()
    {
        _provider.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(Task.FromException<List<MediaSearchEntity>>(new HttpRequestException("network error")));

        var results = await _service.SearchAllAsync("song");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyList_WhenNoMediaMatch()
    {
        _provider.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(Task.FromResult(new List<MediaSearchEntity>()));

        var results = await _service.SearchAllAsync("nonexistent");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_AggregatesResultsFromAllProviders()
    {
        var mediaId1 = Guid.NewGuid();
        var mediaId2 = Guid.NewGuid();
        var entities = new List<MediaSearchEntity>
        {
            new MediaSearchEntity { SourceDisplayName = "FileSystem", Title = "Media1", Artist = "Artist1", Source = "FileSystem", ForeignKey = mediaId1.ToString() },
            new MediaSearchEntity { SourceDisplayName = "FileSystem", Title = "Media2", Artist = "Artist2", Source = "FileSystem", ForeignKey = mediaId2.ToString() }
        };

        _provider.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(Task.FromResult(entities));

        var results = await _service.SearchAllAsync("test");

        Assert.Equal(2, results.Count);
        Assert.Equal("Media1", results[0].Title);
        Assert.Equal("Artist1", results[0].Artist);
        Assert.Equal("Media2", results[1].Title);
        Assert.Equal("Artist2", results[1].Artist);
        Assert.Equal("FileSystem", results[0].Source);
        Assert.Equal(mediaId1.ToString(), results[0].ForeignKey);
        Assert.Equal("FileSystem", results[1].Source);
        Assert.Equal(mediaId2.ToString(), results[1].ForeignKey);
    }

    [Fact]
    public async Task SearchAsync_CallsProviderWithProvidedPageParameters()
    {
        var (service, local, _) = ServiceWithLocalAnd(_provider);
        local.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(Task.FromResult(new List<MediaSearchEntity>()));

        await service.SearchAsync("test", pageNumber: 2, pageSize: 50);

        await local.Received(1).SearchAsync("test", pageNumber: 2, pageSize: 50);
    }

    [Fact]
    public async Task SearchAsync_AsksTheLocalProviderOnly()
    {
        var (service, local, remote) = ServiceWithLocalAnd(_provider);
        local.SearchAsync("song", 0, 0).Returns([Entity(MediaSearchService.LocalSourceName, "local hit")]);

        var results = await service.SearchAsync("song");

        Assert.Equal("local hit", Assert.Single(results).Title);
        await remote.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public async Task SearchAsync_LeavesRemoteProvidersAlone_EvenWhenTheLocalLibraryFindsNothing()
    {
        var (service, local, remote) = ServiceWithLocalAnd(_provider);
        local.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>()).Returns([]);

        var results = await service.SearchAsync("song");

        Assert.Empty(results);
        await remote.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public async Task SearchAllAsync_AsksEveryProvider()
    {
        var (service, local, remote) = ServiceWithLocalAnd(_provider);
        local.SearchAsync("song", 0, 0).Returns([Entity(MediaSearchService.LocalSourceName, "local hit")]);
        remote.SearchAsync("song", 0, 0).Returns([Entity("FileSystem", "remote hit")]);

        var results = await service.SearchAllAsync("song");

        Assert.Equal(2, results.Count);
        await remote.Received(1).SearchAsync("song", 0, 0);
    }

    [Fact]
    public async Task SearchAsync_FindsNothing_WhenNoLocalProviderIsRegistered()
    {
        // Only a remote provider registered: the default search must still not reach for it.
        var results = await _service.SearchAsync("song");

        Assert.Empty(results);
        await _provider.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
    }

    private (MediaSearchService Service, IMediaProvider Local, IMediaProvider Remote) ServiceWithLocalAnd(IMediaProvider remote)
    {
        var local = Substitute.For<IMediaProvider>();
        local.SourceName.Returns(MediaSearchService.LocalSourceName);
        local.DisplayName.Returns("Local");

        var analytics = Substitute.For<IAnalyticsService>();
        analytics.StartActivity(Arg.Any<string>()).Returns(Substitute.For<IAnalyticsActivity>());

        return (new MediaSearchService(_logger, [local, remote], analytics), local, remote);
    }
}
