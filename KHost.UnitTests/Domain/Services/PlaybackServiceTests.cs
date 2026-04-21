using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services;

public class PlaybackServiceTests : IDisposable
{
    private readonly ILogger<PlaybackService> _logger = Substitute.For<ILogger<PlaybackService>>();
    private readonly ISingerQueueService _queueService = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performanceService = Substitute.For<IPerformanceService>();
    private readonly ISingersService _singersService = Substitute.For<ISingersService>();
    private readonly IOptionsMonitor<PlaybackService.ServiceOptions> _options =
        Substitute.For<IOptionsMonitor<PlaybackService.ServiceOptions>>();
    private readonly PlaybackService _service;

    public PlaybackServiceTests()
    {
        _options.CurrentValue.Returns(new PlaybackService.ServiceOptions
        {
            MoveSingerToBottomAfterPerformance = false
        });

        _service = new PlaybackService(_logger, _options, _queueService, _performanceService, _singersService);
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public void NewService_StartsStopped()
    {
        Assert.Equal(PlaybackState.Stopped, _service.State);
        Assert.Null(_service.CurrentPerformance);
        Assert.Equal(TimeSpan.Zero, _service.Position);
    }

    [Fact]
    public async Task Load_SetsCurrentPerformanceAndMedia_AndResetsPosition()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);

        Assert.Same(performance, _service.CurrentPerformance);
        Assert.Same(media, _service.CurrentMedia);
        Assert.Equal(TimeSpan.Zero, _service.Position);
        Assert.Equal(PlaybackState.Stopped, _service.State);
    }

    [Fact]
    public async Task Load_RaisesStateChanged()
    {
        var raised = false;
        _service.StateChanged += (_, _) => raised = true;

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        Assert.True(raised);
    }

    [Fact]
    public async Task PlayAsync_DoesNothing_WhenNoMediaLoaded()
    {
        await _service.PlayAsync();

        Assert.Equal(PlaybackState.Stopped, _service.State);
        await _queueService.DidNotReceive().MoveSingerToStartAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task PlayAsync_TransitionsToPlaying_AndMarksSingerPerforming()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        Assert.Equal(PlaybackState.Playing, _service.State);
        Assert.Equal(performance.SingerId, _service.CurrentlyPerformingSingerId);
    }

    [Fact]
    public async Task PlayAsync_MovesSingerToStart()
    {
        var (performance, media) = CreatePerformance();
        var singer = new Singer { Id = performance.SingerId, Name = "Alice" };
        _queueService.Singers.Returns(new[] { singer }.AsReadOnly());

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _queueService.Received(1).MoveSingerToStartAsync(performance.SingerId);
    }

    [Fact]
    public async Task PlayAsync_IsNoOp_WhenAlreadyPlaying()
    {
        var (performance, media) = CreatePerformance();
        var singer = new Singer { Id = performance.SingerId, Name = "Alice" };
        _queueService.Singers.Returns(new[] { singer }.AsReadOnly());

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        _queueService.ClearReceivedCalls();

        await _service.PlayAsync();

        await _queueService.DidNotReceive().MoveSingerToStartAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Pause_TransitionsToPaused_AndRetainsPerformingId()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.PauseAsync();

        Assert.Equal(PlaybackState.Paused, _service.State);
        Assert.Equal(performance.SingerId, _service.CurrentlyPerformingSingerId);
    }

    [Fact]
    public async Task Pause_IsNoOp_WhenNotPlaying()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        await _service.PauseAsync();

        Assert.Equal(PlaybackState.Stopped, _service.State);
    }

    [Fact]
    public async Task StopAsync_ResetsStateAndClearsCurrentMedia()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.StopAsync();

        Assert.Equal(PlaybackState.Stopped, _service.State);
        Assert.Null(_service.CurrentPerformance);
        Assert.Null(_service.CurrentlyPerformingSingerId);
        Assert.Equal(TimeSpan.Zero, _service.Position);
    }

    [Fact]
    public async Task Load_ResetsPerforming_OfPreviousSinger()
    {
        var (perf1, media1) = CreatePerformance();
        var (perf2, media2) = CreatePerformance();

        await _service.LoadAsync(perf1, media1);
        await _service.PlayAsync();

        await _service.LoadAsync(perf2, media2);

        Assert.Null(_service.CurrentlyPerformingSingerId);
        Assert.Same(perf2, _service.CurrentPerformance);
    }

    [Fact]
    public async Task StopAsync_MovesSingerToEnd_WhenOptionEnabled()
    {
        _options.CurrentValue.Returns(new PlaybackService.ServiceOptions
        {
            MoveSingerToBottomAfterPerformance = true
        });
        var (performance, media) = CreatePerformance();
        var singer = new Singer { Id = performance.SingerId, Name = "Alice" };
        _queueService.Singers.Returns(new[] { singer }.AsReadOnly());

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.StopAsync();

        await _queueService.Received().MoveSingerToEndAsync(performance.SingerId);
        await _queueService.Received().SelectFirstSingerInQueueAsync();
        await _performanceService.Received().DequeueAsync(performance.SingerId, performance.Id);
    }

    [Fact]
    public async Task StopAsync_DequeuesPerformance_WhenMoveToBottomDisabled()
    {
        var (performance, media) = CreatePerformance();
        var singer = new Singer { Id = performance.SingerId, Name = "Alice" };
        _queueService.Singers.Returns(new[] { singer }.AsReadOnly());

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.StopAsync();

        await _performanceService.Received(1).DequeueAsync(performance.SingerId, performance.Id);
    }

    [Fact]
    public async Task StopAsync_DoesNotCallMoveSingerToEnd_WhenMoveToBottomDisabled()
    {
        var (performance, media) = CreatePerformance();
        var singer = new Singer { Id = performance.SingerId, Name = "Alice" };
        _queueService.Singers.Returns(new[] { singer }.AsReadOnly());

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.StopAsync();

        await _queueService.DidNotReceive().MoveSingerToEndAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task StopAsync_DoesNotCallSelectFirst_WhenMoveToBottomDisabled()
    {
        var (performance, media) = CreatePerformance();
        var singer = new Singer { Id = performance.SingerId, Name = "Alice" };
        _queueService.Singers.Returns(new[] { singer }.AsReadOnly());

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.StopAsync();

        await _queueService.DidNotReceive().SelectFirstSingerInQueueAsync();
    }

    [Fact]
    public async Task StopAsync_WhenNothingLoaded_DoesNotCallDequeue()
    {
        await _service.StopAsync();

        await _performanceService.DidNotReceive().DequeueAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    private static (Performance, Media) CreatePerformance()
    {
        var singerId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var performance = new Performance
        {
            Id = Guid.NewGuid(),
            SingerId = singerId,
            MediaId = mediaId,
            CreatedDate = DateTime.Now,
            QueuePosition = 1
        };
        var media = new Media { Id = mediaId, FilePath = "/music/media.mp4", Title = "Media" };
        return (performance, media);
    }
}
