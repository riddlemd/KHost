using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

/// <summary>
/// There are deliberately no foreign keys on a performance: deleting a song, a singer or a venue
/// must leave the record of who sang what standing. That protects history and said nothing about
/// the queue, so a deleted song's row survived it — sitting in a singer's queue looking ordinary,
/// and failing only when somebody tried to play it.
/// </summary>
public class MediaServiceDeleteTests
{
    private readonly IMediaRepository _repository = Substitute.For<IMediaRepository>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    private readonly Guid _mediaId = Guid.NewGuid();

    private MediaService Service() => new(
        NullLogger<MediaService>.Instance, _repository, _broker,
        new ServiceCollection().AddSingleton(_performances).BuildServiceProvider());

    public MediaServiceDeleteTests()
        => _repository.DeleteAsync(Arg.Any<Guid>()).Returns(true);

    private void QueuedOn(params Performance[] performances)
        => _performances
            .ReadByMediaIdAsync(_mediaId, Arg.Any<int>(), Arg.Any<int>(), PerformanceFilter.Queued)
            .Returns(new PaginatedResult<Performance> { Items = [.. performances] });

    private Performance Waiting() => new()
    {
        Id = Guid.NewGuid(),
        SingerId = Guid.NewGuid(),
        MediaId = _mediaId,
        QueuePosition = 1,
    };

    [Fact]
    public async Task DeleteAsync_SongIsWaitingInAQueue_TakesItOutFirst()
    {
        var waiting = Waiting();
        QueuedOn(waiting);

        Assert.True(await Service().DeleteAsync(_mediaId));

        await _performances.Received(1).DeleteAsync(waiting.Id);
    }

    /// <summary>Every queue it is waiting in, not the first one found.</summary>
    [Fact]
    public async Task DeleteAsync_SongIsWaitingForSeveralSingers_TakesItOutOfEach()
    {
        var first = Waiting();
        var second = Waiting();
        QueuedOn(first, second);

        await Service().DeleteAsync(_mediaId);

        await _performances.Received(1).DeleteAsync(first.Id);
        await _performances.Received(1).DeleteAsync(second.Id);
    }

    /// <summary>
    /// Queued rows only. A performance already sung keeps its media id whether or not the file is
    /// still in the library — that is the record this schema drops foreign keys to protect.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_OnlyAsksForTheOnesStillWaiting()
    {
        QueuedOn();

        await Service().DeleteAsync(_mediaId);

        await _performances.Received().ReadByMediaIdAsync(
            _mediaId, Arg.Any<int>(), Arg.Any<int>(), PerformanceFilter.Queued);
        await _performances.DidNotReceive().ReadByMediaIdAsync(
            _mediaId, Arg.Any<int>(), Arg.Any<int>(), PerformanceFilter.UnQueued);
    }

    /// <summary>The host asked for the song to go. A tidy-up that fails must not strand that.</summary>
    [Fact]
    public async Task DeleteAsync_TheTidyUpFails_StillDeletesTheSong()
    {
        _performances
            .ReadByMediaIdAsync(_mediaId, Arg.Any<int>(), Arg.Any<int>(), PerformanceFilter.Queued)
            .Returns<PaginatedResult<Performance>>(_ => throw new InvalidOperationException("no"));

        Assert.True(await Service().DeleteAsync(_mediaId));

        await _repository.Received(1).DeleteAsync(_mediaId);
    }

    /// <summary>
    /// Dequeuing reads the media to see whether a download is still running, so the row has to
    /// outlive the tidy-up by exactly one step.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_ClearsTheQueueBeforeTheSongItself()
    {
        var waiting = Waiting();
        QueuedOn(waiting);

        var order = new List<string>();
        _performances.DeleteAsync(waiting.Id).Returns(_ => { order.Add("dequeued"); return true; });
        _repository.DeleteAsync(_mediaId).Returns(_ => { order.Add("deleted"); return true; });

        await Service().DeleteAsync(_mediaId);

        Assert.Equal(["dequeued", "deleted"], order);
    }
}
