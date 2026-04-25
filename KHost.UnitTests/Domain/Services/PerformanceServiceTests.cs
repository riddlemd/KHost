using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging;

namespace KHost.UnitTests.Domain.Services;

public class PerformanceServiceTests
{
    private readonly List<Performance> _performanceDb = [];
    private readonly IPerformancesRepository _repository = Substitute.For<IPerformancesRepository>();
    private readonly ILogger<PerformanceService> _logger = Substitute.For<ILogger<PerformanceService>>();
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

        _service = new PerformanceService(_logger, _repository);
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

        var performance = await _service.CreateAndEnqueueAsync(new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = mediaId });

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
        var perf1 = await _service.CreateAndEnqueueAsync(new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = Guid.NewGuid() });
        var perf2 = await _service.CreateAndEnqueueAsync(new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = Guid.NewGuid() });

        await _service.DequeueAsync(singerId, perf1.Id);

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Single(queued.Items);
        Assert.Equal(perf2.Id, queued.Items[0].Id);
    }

    [Fact]
    public async Task MoveUpInQueueAsync_SwapsWithPrevious()
    {
        var singerId = Guid.NewGuid();
        var perf1 = await _service.CreateAndEnqueueAsync(new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = Guid.NewGuid() });
        var perf2 = await _service.CreateAndEnqueueAsync(new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = Guid.NewGuid() });

        await _service.MoveUpInQueueAsync(singerId, perf2.Id);

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Equal(perf2.Id, queued.Items[0].Id);
        Assert.Equal(perf1.Id, queued.Items[1].Id);
    }

    [Fact]
    public async Task MoveDownInQueueAsync_SwapsWithNext()
    {
        var singerId = Guid.NewGuid();
        var perf1 = await _service.CreateAndEnqueueAsync(new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = Guid.NewGuid() });
        var perf2 = await _service.CreateAndEnqueueAsync(new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = Guid.NewGuid() });

        await _service.MoveDownInQueueAsync(singerId, perf1.Id);

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Equal(perf2.Id, queued.Items[0].Id);
        Assert.Equal(perf1.Id, queued.Items[1].Id);
    }

    [Fact]
    public async Task MoveToEndOfQueueAsync_MovesToEnd()
    {
        var singerId = Guid.NewGuid();
        var perf1 = await _service.CreateAndEnqueueAsync(new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = Guid.NewGuid() });
        var perf2 = await _service.CreateAndEnqueueAsync(new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = Guid.NewGuid() });
        var perf3 = await _service.CreateAndEnqueueAsync(new Performance { Id = Guid.NewGuid(), SingerId = singerId, MediaId = Guid.NewGuid() });

        await _service.MoveToEndOfQueueAsync(singerId, perf1.Id);

        var queued = await _service.ReadBySingerIdAsync(singerId, filter: PerformanceFilter.Queued);
        Assert.Equal(perf2.Id, queued.Items[0].Id);
        Assert.Equal(perf3.Id, queued.Items[1].Id);
        Assert.Equal(perf1.Id, queued.Items[2].Id);
    }
}
