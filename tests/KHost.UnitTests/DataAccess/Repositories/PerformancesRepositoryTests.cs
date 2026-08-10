using KHost.Abstractions.Models;
using KHost.DataAccess.Repositories;

namespace KHost.UnitTests.DataAccess.Repositories;

public class PerformancesRepositoryTests
{
    private static IQueryable<Performance> MakeQuery(params int?[] queuePositions)
        => queuePositions.Select(q => new Performance { QueuePosition = q }).AsQueryable();

    [Fact]
    public void ApplyFilter_ReturnsOnlyQueued_WhenQueuedFlagSet()
    {
        var query = MakeQuery(1, 2, null, null);

        var result = PerformancesRepository.ApplyFilter(query, PerformanceFilter.Queued).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.NotNull(p.QueuePosition));
    }

    [Fact]
    public void ApplyFilter_ReturnsOnlyUnqueued_WhenUnQueuedFlagSet()
    {
        var query = MakeQuery(1, null, null);

        var result = PerformancesRepository.ApplyFilter(query, PerformanceFilter.UnQueued).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.Null(p.QueuePosition));
    }

    [Fact]
    public void ApplyFilter_ReturnsAll_WhenAllFlagSet()
    {
        var query = MakeQuery(1, null, 3);

        var result = PerformancesRepository.ApplyFilter(query, PerformanceFilter.All).ToList();

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ApplyFilter_ReturnsAll_WhenBothFlagsExplicitlySet()
    {
        var query = MakeQuery(1, null);

        var result = PerformancesRepository.ApplyFilter(query, PerformanceFilter.Queued | PerformanceFilter.UnQueued).ToList();

        Assert.Equal(2, result.Count);
    }
}
