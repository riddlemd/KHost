using KHost.Abstractions.Interactions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Domain.Services.MediaProviders;
using Microsoft.Extensions.Logging;

namespace KHost.UnitTests.Domain.Services.MediaProviders;

public class DefaultMediaProviderTests
{
    private readonly ILogger<DefaultMediaProvider> _logger = Substitute.For<ILogger<DefaultMediaProvider>>();
    private readonly IPerformanceService _performanceService = Substitute.For<IPerformanceService>();
    private readonly ISingerQueueService _singerQueueService = Substitute.For<ISingerQueueService>();
    private readonly IMediaRepository _repository = Substitute.For<IMediaRepository>();
    private readonly IInteractionDispatcher _interactions = Substitute.For<IInteractionDispatcher>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly DefaultMediaProvider _service;
    private readonly List<Media> _mediaStore = [];

    public DefaultMediaProviderTests()
    {
        _repository.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(call =>
            {
                var query = call.ArgAt<string>(0);
                var pageNumber = call.ArgAt<int>(1);
                var pageSize = call.ArgAt<int>(2);
                var filtered = string.IsNullOrWhiteSpace(query)
                    ? _mediaStore
                    : _mediaStore.Where(m =>
                        m.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        m.Artist.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                return Task.FromResult(new PaginatedResult<Media>
                {
                    Items = filtered,
                    TotalCount = filtered.Count,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                });
            });

        _performanceService.CreateAndEnqueueAsync(Arg.Any<Performance>())
            .Returns(args => Task.FromResult((Performance)args[0]));

        _service = new DefaultMediaProvider(_logger, _performanceService, _singerQueueService, _repository, _interactions, _mediaService);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmpty_WhenRepositoryReturnsNoResults()
    {
        var result = await _service.SearchAsync("rock");

        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchAsync_DelegatesToRepository_WithCorrectQuery()
    {
        _mediaStore.Add(new Media { Title = "Bohemian Rhapsody", Artist = "Queen", FilePath = "/a.mp3" });

        await _service.SearchAsync("rock");

        await _repository.Received(1).SearchAsync("rock", Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public async Task SearchAsync_DelegatesToRepository_WithPageParameters()
    {
        await _service.SearchAsync("any", pageNumber: 2, pageSize: 25);

        await _repository.Received(1).SearchAsync(Arg.Any<string>(), 2, 25);
    }

    [Fact]
    public async Task SearchAsync_MapsDisplayName_AsArtistDashTitle()
    {
        _mediaStore.Add(new Media { Title = "Bohemian Rhapsody", Artist = "Queen", FilePath = "/a.mp3" });

        var result = await _service.SearchAsync("Queen");

        Assert.Equal("Queen - Bohemian Rhapsody", result[0].DisplayName);
    }

    [Fact]
    public async Task SearchAsync_SetsSource_ToDefaultMediaProvider()
    {
        _mediaStore.Add(new Media { Title = "Track", Artist = "Artist", FilePath = "/a.mp3" });

        var result = await _service.SearchAsync("Track");

        Assert.Equal("DefaultMediaProvider", result[0].Source);
    }

    [Fact]
    public async Task SearchAsync_SetsForeignKey_ToMediaIdString()
    {
        var media = new Media { Title = "Track", Artist = "Artist", FilePath = "/a.mp3" };
        _mediaStore.Add(media);

        var result = await _service.SearchAsync("Track");

        Assert.Equal(media.Id.ToString(), result[0].ForeignKey);
    }

    [Fact]
    public async Task SearchAsync_ReturnsOneEntityPerMediaItem()
    {
        _mediaStore.Add(new Media { Title = "Song A", Artist = "Band", FilePath = "/a.mp3" });
        _mediaStore.Add(new Media { Title = "Song B", Artist = "Band", FilePath = "/b.mp3" });
        _mediaStore.Add(new Media { Title = "Song C", Artist = "Band", FilePath = "/c.mp3" });

        var result = await _service.SearchAsync("Band");

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task EnqueueAsync_DoesNothing_WhenNoSingerSelected()
    {
        _singerQueueService.SelectedSingerId.Returns((Guid?)null);

        await _service.EnqueueAsync(new MediaSearchEntity { SourceDisplayName = "test", Source = "test", ForeignKey = Guid.NewGuid().ToString(), DisplayName = "test" });

        await _performanceService.DidNotReceive().CreateAndEnqueueAsync(Arg.Any<Performance>());
    }

    [Fact]
    public async Task EnqueueAsync_CallsCreateAndEnqueue_WithCorrectMediaId()
    {
        var singerId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        _singerQueueService.SelectedSingerId.Returns(singerId);

        await _service.EnqueueAsync(new MediaSearchEntity { SourceDisplayName = "test", Source = "test", ForeignKey = mediaId.ToString(), DisplayName = "test" });

        await _performanceService.Received(1)
            .CreateAndEnqueueAsync(Arg.Is<Performance>(p => p.MediaId == mediaId));
    }

    [Fact]
    public async Task EnqueueAsync_CallsCreateAndEnqueue_WithCorrectSingerId()
    {
        var singerId = Guid.NewGuid();
        _singerQueueService.SelectedSingerId.Returns(singerId);

        await _service.EnqueueAsync(new MediaSearchEntity { SourceDisplayName = "test", Source = "test", ForeignKey = Guid.NewGuid().ToString(), DisplayName = "test" });

        await _performanceService.Received(1)
            .CreateAndEnqueueAsync(Arg.Is<Performance>(p => p.SingerId == singerId));
    }
}
