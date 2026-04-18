using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Models;
using KHost.Domain.Services;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services;

public class PlaybackServiceTests : IDisposable
{
    private readonly ISingerQueueService _queueService = Substitute.For<ISingerQueueService>();
    private readonly IOptionsMonitor<PlaybackService.ServiceOptions> _options =
        Substitute.For<IOptionsMonitor<PlaybackService.ServiceOptions>>();
    private readonly PlaybackService _service;

    public PlaybackServiceTests()
    {
        _options.CurrentValue.Returns(new PlaybackService.ServiceOptions
        {
            MoveSingerToBottomAfterPerformance = false
        });

        _service = new PlaybackService(_options, _queueService);
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public void NewService_StartsStopped()
    {
        Assert.Equal(PlaybackState.Stopped, _service.State);
        Assert.Null(_service.CurrentQueuedSong);
        Assert.Equal(TimeSpan.Zero, _service.Position);
    }

    [Fact]
    public void Load_SetsCurrentSong_AndResetsPosition()
    {
        var queued = CreateQueuedSong();

        _service.Load(queued);

        Assert.Same(queued, _service.CurrentQueuedSong);
        Assert.Equal(TimeSpan.Zero, _service.Position);
        Assert.Equal(PlaybackState.Stopped, _service.State);
    }

    [Fact]
    public void Load_RaisesStateChanged()
    {
        var raised = false;
        _service.StateChanged += () => raised = true;

        _service.Load(CreateQueuedSong());

        Assert.True(raised);
    }

    [Fact]
    public async Task PlayAsync_DoesNothing_WhenNoSongLoaded()
    {
        await _service.PlayAsync();

        Assert.Equal(PlaybackState.Stopped, _service.State);
        await _queueService.DidNotReceive().MoveSingerToStartAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task PlayAsync_TransitionsToPlaying_AndMarksSingerPerforming()
    {
        var queued = CreateQueuedSong();
        _service.Load(queued);

        await _service.PlayAsync();

        Assert.Equal(PlaybackState.Playing, _service.State);
        Assert.Equal(QueuedSongStatus.Playing, queued.Status);
        Assert.True(queued.Singer.IsPerforming);
    }

    [Fact]
    public async Task PlayAsync_MovesSingerToStart()
    {
        var queued = CreateQueuedSong();
        _service.Load(queued);

        await _service.PlayAsync();

        await _queueService.Received(1).MoveSingerToStartAsync(queued.Singer.Id);
    }

    [Fact]
    public async Task PlayAsync_IsNoOp_WhenAlreadyPlaying()
    {
        var queued = CreateQueuedSong();
        _service.Load(queued);
        await _service.PlayAsync();
        _queueService.ClearReceivedCalls();

        await _service.PlayAsync();

        await _queueService.DidNotReceive().MoveSingerToStartAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Pause_StopsPlayback_AndClearsPerformingFlag()
    {
        var queued = CreateQueuedSong();
        _service.Load(queued);
        await _service.PlayAsync();

        _service.Pause();

        Assert.Equal(PlaybackState.Paused, _service.State);
        Assert.False(queued.Singer.IsPerforming);
    }

    [Fact]
    public void Pause_IsNoOp_WhenNotPlaying()
    {
        var queued = CreateQueuedSong();
        _service.Load(queued);

        _service.Pause();

        Assert.Equal(PlaybackState.Stopped, _service.State);
    }

    [Fact]
    public async Task StopAsync_ResetsStateAndClearsCurrentSong()
    {
        var queued = CreateQueuedSong();
        _service.Load(queued);
        await _service.PlayAsync();

        await _service.StopAsync();

        Assert.Equal(PlaybackState.Stopped, _service.State);
        Assert.Null(_service.CurrentQueuedSong);
        Assert.False(queued.Singer.IsPerforming);
        Assert.Equal(TimeSpan.Zero, _service.Position);
    }

    [Fact]
    public async Task Load_ResetsPerforming_OfPreviousSinger()
    {
        var first = CreateQueuedSong();
        _service.Load(first);
        await _service.PlayAsync();

        var second = CreateQueuedSong();
        _service.Load(second);

        Assert.False(first.Singer.IsPerforming);
        Assert.Same(second, _service.CurrentQueuedSong);
    }

    [Fact]
    public async Task StopAsync_MovesSingerToEnd_WhenOptionEnabled()
    {
        _options.CurrentValue.Returns(new PlaybackService.ServiceOptions
        {
            MoveSingerToBottomAfterPerformance = true
        });
        var queued = CreateQueuedSong();
        _service.Load(queued);
        await _service.PlayAsync();

        await _service.StopAsync();

        await _queueService.Received().MoveSingerToEndAsync(Arg.Any<Guid>());
        await _queueService.Received().SelectFirstSingerInQueueAsync();
    }

    private static QueuedSong CreateQueuedSong()
    {
        var singer = new Singer { Id = Guid.NewGuid(), Name = "Alice" };
        var song = new Song { FilePath = "/music/song.mp4", Title = "Song" };
        return new QueuedSong { Singer = singer, Song = song };
    }
}
