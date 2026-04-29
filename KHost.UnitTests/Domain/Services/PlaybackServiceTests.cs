using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging;

namespace KHost.UnitTests.Domain.Services;

public class PlaybackServiceTests : IDisposable
{
    private readonly ILogger<PlaybackService> _logger = Substitute.For<ILogger<PlaybackService>>();
    private readonly ISingerQueueService _queueService = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performanceService = Substitute.For<IPerformanceService>();
    private readonly IVenuesService _venuesService = Substitute.For<IVenuesService>();
    private readonly PlaybackService _service;

    public PlaybackServiceTests()
    {
        _venuesService.ReadSelectedVenueAsync()
            .Returns(new Venue { Id = Guid.NewGuid(), Name = "Test Venue", Settings = new Venue.VenueSettings { MoveSingerToBottomAfterPerformance = false } });

        var analytics = Substitute.For<IAnalyticsService>();
        _service = new PlaybackService(_logger, _queueService, _performanceService, _venuesService, analytics);
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
        await _queueService.DidNotReceive().MoveUserToStartAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task PlayAsync_TransitionsToPlaying_AndMarksUserPerforming()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        Assert.Equal(PlaybackState.Playing, _service.State);
        Assert.Equal(performance.SingerId, _service.CurrentlyPerformingUserId);
    }

    [Fact]
    public async Task PlayAsync_MovesUserToStart()
    {
        var (performance, media) = CreatePerformance();
        var user = new KHostUser { Id = performance.SingerId, Name = "Alice" };
        _queueService.Users.Returns(new[] { user }.AsReadOnly());

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _queueService.Received(1).MoveUserToStartAsync(performance.SingerId);
    }

    [Fact]
    public async Task PlayAsync_IsNoOp_WhenAlreadyPlaying()
    {
        var (performance, media) = CreatePerformance();
        var user = new KHostUser { Id = performance.SingerId, Name = "Alice" };
        _queueService.Users.Returns(new[] { user }.AsReadOnly());

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        _queueService.ClearReceivedCalls();

        await _service.PlayAsync();

        await _queueService.DidNotReceive().MoveUserToStartAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Pause_TransitionsToPaused_AndRetainsPerformingId()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.PauseAsync();

        Assert.Equal(PlaybackState.Paused, _service.State);
        Assert.Equal(performance.SingerId, _service.CurrentlyPerformingUserId);
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
        Assert.Null(_service.CurrentlyPerformingUserId);
        Assert.Equal(TimeSpan.Zero, _service.Position);
    }

    [Fact]
    public async Task Load_ResetsPerforming_OfPreviousUser()
    {
        var (perf1, media1) = CreatePerformance();
        var (perf2, media2) = CreatePerformance();

        await _service.LoadAsync(perf1, media1);
        await _service.PlayAsync();

        await _service.LoadAsync(perf2, media2);

        Assert.Null(_service.CurrentlyPerformingUserId);
        Assert.Same(perf2, _service.CurrentPerformance);
    }

    [Fact]
    public async Task StopAsync_MovesUserToEnd_WhenOptionEnabled()
    {
        var venue = new Venue
        {
            Id = Guid.NewGuid(),
            Name = "Test Venue",
            Settings = new Venue.VenueSettings { MoveSingerToBottomAfterPerformance = true }
        };
        _venuesService.ReadSelectedVenueAsync().Returns(venue);

        var (performance, media) = CreatePerformance();
        var user = new KHostUser { Id = performance.SingerId, Name = "Alice" };
        _queueService.Users.Returns(new[] { user }.AsReadOnly());

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.StopAsync();

        await _queueService.Received().MoveUserToEndAsync(performance.SingerId);
        await _queueService.Received().SelectFirstUserInQueueAsync();
        await _performanceService.Received().DequeueAsync(performance.SingerId, performance.Id);
    }

    [Fact]
    public async Task StopAsync_DequeuesPerformance_WhenMoveToBottomDisabled()
    {
        var (performance, media) = CreatePerformance();
        var user = new KHostUser { Id = performance.SingerId, Name = "Alice" };
        _queueService.Users.Returns(new[] { user }.AsReadOnly());

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.StopAsync();

        await _performanceService.Received(1).DequeueAsync(performance.SingerId, performance.Id);
    }

    [Fact]
    public async Task StopAsync_DoesNotCallMoveUserToEnd_WhenMoveToBottomDisabled()
    {
        var (performance, media) = CreatePerformance();
        var user = new KHostUser { Id = performance.SingerId, Name = "Alice" };
        _queueService.Users.Returns(new[] { user }.AsReadOnly());

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.StopAsync();

        await _queueService.DidNotReceive().MoveUserToEndAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task StopAsync_DoesNotCallSelectFirst_WhenMoveToBottomDisabled()
    {
        var (performance, media) = CreatePerformance();
        var user = new KHostUser { Id = performance.SingerId, Name = "Alice" };
        _queueService.Users.Returns(new[] { user }.AsReadOnly());

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.StopAsync();

        await _queueService.DidNotReceive().SelectFirstUserInQueueAsync();
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
