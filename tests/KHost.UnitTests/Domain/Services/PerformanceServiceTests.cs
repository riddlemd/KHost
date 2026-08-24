using KHost.Abstractions.Interactions;
using KHost.Abstractions.Interactions.Requests;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;
using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

public class PerformanceServiceTests
{
    private readonly List<Performance> _performanceDb = [];
    private readonly IPerformancesRepository _repository = Substitute.For<IPerformancesRepository>();
    private readonly ILogger<PerformanceService> _logger = Substitute.For<ILogger<PerformanceService>>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly IVenuesService _venuesService = Substitute.For<IVenuesService>();
    private readonly IInteractionDispatcher _interactions = Substitute.For<IInteractionDispatcher>();
    private readonly IDownloadsService _downloadsService = Substitute.For<IDownloadsService>();
    private readonly Venue _venue = new() { Name = "Test Venue" };
    private readonly PerformanceService _service;

    public PerformanceServiceTests()
    {
        _repository.CreateAsync(Arg.Any<Performance>())
            .Returns(args =>
            {
                var perf = (Performance)args[0];
                _performanceDb.Add(perf);
                return Task.FromResult(perf);
            });

        _repository.ReadAsync(Arg.Any<Guid>())
            .Returns(args => Task.FromResult(_performanceDb.FirstOrDefault(p => p.Id == (Guid)args[0])));

        _repository.UpdateAsync(Arg.Any<Performance>())
            .Returns(args =>
            {
                var perf = (Performance)args[0];
                var existing = _performanceDb.FirstOrDefault(p => p.Id == perf.Id);
                if (existing != null)
                {
                    existing.QueuePosition = perf.QueuePosition;
                }
                return Task.FromResult(perf);
            });

        _repository.ReadNextQueuePositionForSingerAsync(Arg.Any<Guid>())
            .Returns(args =>
            {
                var singerId = (Guid)args[0];
                var maxPosition = _performanceDb
                    .Where(p => p.SingerId == singerId && p.QueuePosition.HasValue)
                    .Max(p => p.QueuePosition) ?? 0;
                return Task.FromResult(maxPosition + 1);
            });

        _repository.ReadQueuedAsync()
            .Returns(_ =>
            {
                var queued = _performanceDb
                    .Where(p => p.QueuePosition.HasValue)
                    .OrderBy(p => p.QueuePosition)
                    .ToList();
                return Task.FromResult(queued);
            });

        _repository.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>())
            .Returns(_ =>
            {
                var allPerfs = new PaginatedResult<Performance>
                {
                    Items = _performanceDb,
                    PageNumber = 1,
                    PageSize = 1000,
                    TotalCount = _performanceDb.Count
                };
                return Task.FromResult(allPerfs);
            });

        _repository.ReadBySingerIdAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<PerformanceFilter>())
            .Returns(args =>
            {
                var singerId = (Guid)args[0];
                var filter = (PerformanceFilter)args[3];

                var query = _performanceDb.Where(p => p.SingerId == singerId);

                if (filter.HasFlag(PerformanceFilter.Queued) && !filter.HasFlag(PerformanceFilter.UnQueued))
                    query = query.Where(p => p.QueuePosition.HasValue);
                else if (!filter.HasFlag(PerformanceFilter.Queued) && filter.HasFlag(PerformanceFilter.UnQueued))
                    query = query.Where(p => !p.QueuePosition.HasValue);

                var items = query.OrderBy(p => p.QueuePosition).ToList();
                return Task.FromResult(new PaginatedResult<Performance>
                {
                    Items = items,
                    TotalCount = items.Count,
                    PageNumber = 1,
                    PageSize = 0
                });
            });

        _repository.ReadByMediaIdAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<PerformanceFilter>())
            .Returns(args =>
            {
                var mediaId = (Guid)args[0];
                var filter = (PerformanceFilter)args[3];

                var query = _performanceDb.Where(p => p.MediaId == mediaId);

                if (filter.HasFlag(PerformanceFilter.Queued) && !filter.HasFlag(PerformanceFilter.UnQueued))
                    query = query.Where(p => p.QueuePosition.HasValue);
                else if (!filter.HasFlag(PerformanceFilter.Queued) && filter.HasFlag(PerformanceFilter.UnQueued))
                    query = query.Where(p => !p.QueuePosition.HasValue);

                var items = query.ToList();
                return Task.FromResult(new PaginatedResult<Performance>
                {
                    Items = items,
                    TotalCount = items.Count,
                    PageNumber = 1,
                    PageSize = 0
                });
            });

        _repository.DeleteAsync(Arg.Any<Guid>())
            .Returns(args =>
            {
                var id = (Guid)args[0];
                var existing = _performanceDb.FirstOrDefault(p => p.Id == id);
                if (existing is null) return Task.FromResult(false);

                _performanceDb.Remove(existing);
                return Task.FromResult(true);
            });

        _venuesService.ReadSelectedVenueAsync().Returns(_venue);

        _service = new PerformanceService(_logger, _repository, _mediaService, _venuesService, _interactions, _downloadsService);
    }

    [Fact]
    public async Task NewService_HasEmptyQueues()
    {
        var singerId = Guid.NewGuid();
        var performances = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);

        Assert.Empty(performances.Items);
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_AddsMediaToSingerQueue()
    {
        var singerId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();

        var performance = Assert.IsType<Performance>(
            await _service.CreateAndEnqueueAsync(new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = mediaId }));

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Single(queued.Items);
        Assert.Equal(performance.Id, queued.Items[0].Id);
        Assert.Equal(singerId, queued.Items[0].SingerId);
        Assert.Equal(mediaId, queued.Items[0].MediaId);
        Assert.Equal(1, queued.Items[0].QueuePosition);
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_RaisesStateChanged()
    {
        var raised = false;
        _service.StateChanged += (_, _) => raised = true;

        await _service.CreateAndEnqueueAsync(new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = Guid.NewGuid() });

        Assert.True(raised);
    }

    [Fact]
    public async Task DequeueAsync_RemovesMedia()
    {
        var singerId = Guid.NewGuid();
        var perf1 = await EnqueueForAsync(singerId);
        var perf2 = await EnqueueForAsync(singerId);

        await _service.DequeueAsync(singerId, perf1.Id);

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Single(queued.Items);
        Assert.Equal(perf2.Id, queued.Items[0].Id);
    }

    [Fact]
    public async Task DeleteAsync_RemovesADownloadingPerformancesMedia_CancelsThatMediasImport()
    {
        var singerId = Guid.NewGuid();
        var performance = await EnqueueForAsync(singerId);
        _mediaService.ReadAsync(performance.MediaId).Returns(new Media { Id = performance.MediaId, FilePath = "/downloads/song.mp4", Title = "Song", Status = MediaStatus.Downloading });

        await _service.DeleteAsync(performance.Id);

        await _downloadsService.Received(1).CancelAsync(performance.MediaId);
    }

    [Fact]
    public async Task DeleteAsync_RemovesAReadyPerformancesMedia_DoesNotCancelAnyImport()
    {
        var singerId = Guid.NewGuid();
        var performance = await EnqueueForAsync(singerId);
        _mediaService.ReadAsync(performance.MediaId).Returns(new Media { Id = performance.MediaId, FilePath = "/downloads/song.mp4", Title = "Song", Status = MediaStatus.Ready });

        await _service.DeleteAsync(performance.Id);

        await _downloadsService.DidNotReceive().CancelAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task DeleteAsync_RemovesThePerformance_RegardlessOfMediaStatus()
    {
        var singerId = Guid.NewGuid();
        var performance = await EnqueueForAsync(singerId);
        _mediaService.ReadAsync(performance.MediaId).Returns(new Media { Id = performance.MediaId, FilePath = "/downloads/song.mp4", Title = "Song", Status = MediaStatus.Ready });

        var deleted = await _service.DeleteAsync(performance.Id);

        Assert.True(deleted);
        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Empty(queued.Items);
    }

    [Fact]
    public async Task DeleteAsync_NoSuchPerformance_DoesNotCancelAnyImport()
    {
        await _service.DeleteAsync(Guid.NewGuid());

        await _downloadsService.DidNotReceive().CancelAsync(Arg.Any<Guid>());
    }

    // A dedicated repository substitute, not the shared one: reconfiguring the shared repository's
    // DeleteAsync for one specific id re-triggers its existing Arg.Any callback as a side effect
    // (NSubstitute routes the call once while recording it, before the new Returns takes over),
    // which would silently delete the row out from under this test before it ever calls DeleteAsync.
    [Fact]
    public async Task DeleteAsync_RepositoryDeleteFails_DoesNotCancelAnyImport()
    {
        var repository = Substitute.For<IPerformancesRepository>();
        var performance = new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = Guid.NewGuid() };
        repository.ReadAsync(performance.Id).Returns(performance);
        repository.DeleteAsync(performance.Id).Returns(false);
        _mediaService.ReadAsync(performance.MediaId).Returns(new Media { Id = performance.MediaId, FilePath = "/downloads/song.mp4", Title = "Song", Status = MediaStatus.Downloading });

        var service = new PerformanceService(_logger, repository, _mediaService, _venuesService, _interactions, _downloadsService);

        var deleted = await service.DeleteAsync(performance.Id);

        Assert.False(deleted);
        await _downloadsService.DidNotReceive().CancelAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task MoveUpInQueueAsync_SwapsWithPrevious()
    {
        var singerId = Guid.NewGuid();
        var perf1 = await EnqueueForAsync(singerId);
        var perf2 = await EnqueueForAsync(singerId);

        await _service.MoveUpInQueueAsync(singerId, perf2.Id);

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Equal(perf2.Id, queued.Items[0].Id);
        Assert.Equal(perf1.Id, queued.Items[1].Id);
    }

    [Fact]
    public async Task MoveDownInQueueAsync_SwapsWithNext()
    {
        var singerId = Guid.NewGuid();
        var perf1 = await EnqueueForAsync(singerId);
        var perf2 = await EnqueueForAsync(singerId);

        await _service.MoveDownInQueueAsync(singerId, perf1.Id);

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Equal(perf2.Id, queued.Items[0].Id);
        Assert.Equal(perf1.Id, queued.Items[1].Id);
    }

    [Fact]
    public async Task MoveToIndexAsync_DropsTheSongAtTheIndexAndClosesTheGap()
    {
        var singerId = Guid.NewGuid();
        var first = await EnqueueForAsync(singerId);
        var second = await EnqueueForAsync(singerId);
        var third = await EnqueueForAsync(singerId);

        await _service.MoveToIndexAsync(singerId, first.Id, 2);

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Equal([second.Id, third.Id, first.Id], queued.Items.Select(p => p.Id));
        // Positions, not just order: they are what everything else reads the queue back by, and
        // enqueueing takes the next one from their maximum.
        Assert.Equal([1, 2, 3], queued.Items.Select(p => p.QueuePosition));
    }

    [Fact]
    public async Task MoveToIndexAsync_MovesASongUpTheQueue()
    {
        var singerId = Guid.NewGuid();
        var first = await EnqueueForAsync(singerId);
        var second = await EnqueueForAsync(singerId);
        var third = await EnqueueForAsync(singerId);

        await _service.MoveToIndexAsync(singerId, third.Id, 0);

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Equal([third.Id, first.Id, second.Id], queued.Items.Select(p => p.Id));
    }

    // The row a drag lands on belongs to the whole table, and the queue can shrink mid-drag.
    [Theory]
    [InlineData(-3)]
    [InlineData(99)]
    public async Task MoveToIndexAsync_ClampsAnIndexPastEitherEnd(int newIndex)
    {
        var singerId = Guid.NewGuid();
        var first = await EnqueueForAsync(singerId);
        var second = await EnqueueForAsync(singerId);

        await _service.MoveToIndexAsync(singerId, first.Id, newIndex);

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        var expected = newIndex < 0 ? new[] { first.Id, second.Id } : [second.Id, first.Id];
        Assert.Equal(expected, queued.Items.Select(p => p.Id));
    }

    [Fact]
    public async Task MoveToIndexAsync_LeavesAnotherSingersQueueAlone()
    {
        var singerId = Guid.NewGuid();
        var otherSinger = Guid.NewGuid();
        var mine = await EnqueueForAsync(singerId);
        await EnqueueForAsync(singerId);
        await EnqueueForAsync(otherSinger);
        await EnqueueForAsync(otherSinger);
        // Read now, not after: the repository hands back the same instances, so comparing the rows
        // to themselves once the move has run would pass however badly they were renumbered. Two
        // songs each, because renumbering everyone leaves the first of them on position 1 anyway.
        var before = (await _service.ReadBySingerIdAsync(otherSinger, filter: PerformanceFilter.Queued))
            .Items.Select(p => p.QueuePosition).ToList();

        await _service.MoveToIndexAsync(singerId, mine.Id, 1);

        var others = await _service.ReadBySingerIdAsync(otherSinger, filter: PerformanceFilter.Queued);
        Assert.Equal(before, others.Items.Select(p => p.QueuePosition));
    }

    [Fact]
    public async Task MoveToIndexAsync_IgnoresAPerformanceThatIsNotQueuedForThatSinger()
    {
        var singerId = Guid.NewGuid();
        var first = await EnqueueForAsync(singerId);
        var second = await EnqueueForAsync(singerId);

        await _service.MoveToIndexAsync(singerId, Guid.NewGuid(), 0);

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Equal([first.Id, second.Id], queued.Items.Select(p => p.Id));
    }

    [Fact]
    public async Task MoveToEndOfQueueAsync_MovesToEnd()
    {
        var singerId = Guid.NewGuid();
        var perf1 = await EnqueueForAsync(singerId);
        var perf2 = await EnqueueForAsync(singerId);
        var perf3 = await EnqueueForAsync(singerId);

        await _service.MoveToEndOfQueueAsync(singerId, perf1.Id);

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Equal(perf2.Id, queued.Items[0].Id);
        Assert.Equal(perf3.Id, queued.Items[1].Id);
        Assert.Equal(perf1.Id, queued.Items[2].Id);
    }

    [Fact]
    public async Task DeleteAllQueuedAsync_DelegatesToRepository()
    {
        _repository.DeleteAllQueuedAsync().Returns(Task.CompletedTask);

        await _service.DeleteAllQueuedAsync();

        await _repository.Received(1).DeleteAllQueuedAsync();
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_DoesNotWarn_WhenVenueHasWarningDisabled()
    {
        var singerId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        await EnqueueMediaAsync(singerId, mediaId);

        await EnqueueMediaAsync(Guid.NewGuid(), mediaId);

        await _interactions.DidNotReceive().RequestAsync(Arg.Any<ConfirmDuplicateSongRequest>());
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_Warns_WhenSongIsAlreadyQueued()
    {
        _venue.Settings.WarnOnDuplicateSong = true;
        var mediaId = Guid.NewGuid();
        await EnqueueMediaAsync(Guid.NewGuid(), mediaId);

        await EnqueueMediaAsync(Guid.NewGuid(), mediaId);

        await _interactions.Received(1).RequestAsync(
            Arg.Is<ConfirmDuplicateSongRequest>(r => r.TimesAlreadyQueued == 1 && r.SungWithinHours == null));
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_DoesNotWarn_WhenSongIsNotADuplicate()
    {
        _venue.Settings.WarnOnDuplicateSong = true;

        await EnqueueMediaAsync(Guid.NewGuid(), Guid.NewGuid());

        await _interactions.DidNotReceive().RequestAsync(Arg.Any<ConfirmDuplicateSongRequest>());
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_ReturnsNullAndQueuesNothing_WhenWarningDeclined()
    {
        _venue.Settings.WarnOnDuplicateSong = true;
        var mediaId = Guid.NewGuid();
        var singerId = Guid.NewGuid();
        await EnqueueMediaAsync(singerId, mediaId);

        _interactions.RequestAsync(Arg.Any<ConfirmDuplicateSongRequest>()).Returns(false);
        var declined = await EnqueueMediaAsync(singerId, mediaId);

        Assert.Null(declined);
        Assert.Single((await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued)).Items);
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_Warns_WhenSongWasSungInsideTheWindow()
    {
        _venue.Settings.WarnOnDuplicateSong = true;
        _venue.Settings.DuplicateSongWindowHours = 4;
        var mediaId = Guid.NewGuid();

        _performanceDb.Add(new Performance
        {
            Id = Guid.NewGuid(),
            SingerId = Guid.NewGuid(),
            MediaId = mediaId,
            QueuePosition = null,
            CreatedDate = DateTime.UtcNow.AddHours(-2)
        });

        await EnqueueMediaAsync(Guid.NewGuid(), mediaId);

        await _interactions.Received(1).RequestAsync(
            Arg.Is<ConfirmDuplicateSongRequest>(r => r.SungWithinHours == 2));
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_DoesNotWarn_WhenSongWasSungOutsideTheWindow()
    {
        _venue.Settings.WarnOnDuplicateSong = true;
        _venue.Settings.DuplicateSongWindowHours = 4;
        var mediaId = Guid.NewGuid();

        _performanceDb.Add(new Performance
        {
            Id = Guid.NewGuid(),
            SingerId = Guid.NewGuid(),
            MediaId = mediaId,
            QueuePosition = null,
            CreatedDate = DateTime.UtcNow.AddHours(-9)
        });

        await EnqueueMediaAsync(Guid.NewGuid(), mediaId);

        await _interactions.DidNotReceive().RequestAsync(Arg.Any<ConfirmDuplicateSongRequest>());
    }

    private async Task<Performance> EnqueueForAsync(Guid singerId)
        => Assert.IsType<Performance>(await EnqueueMediaAsync(singerId, Guid.NewGuid()));

    private async Task<Performance?> EnqueueMediaAsync(Guid singerId, Guid mediaId)
        => await _service.CreateAndEnqueueAsync(
            new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = mediaId });
}
