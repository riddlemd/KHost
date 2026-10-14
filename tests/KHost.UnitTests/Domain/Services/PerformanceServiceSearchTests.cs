using KHost.Abstractions.Interactions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.DataAccess.Repositories;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.UnitTests.DataAccess;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

/// <summary>Search on a row that holds no text, through the real repository.</summary>
public class PerformanceServiceSearchTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly PerformanceService _service;

    public PerformanceServiceSearchTests()
    {
        var repository = new PerformancesRepository(_database, NullLogger<BaseRepository<Performance>>.Instance);

        _service = new PerformanceService(
            NullLogger<PerformanceService>.Instance,
            repository,
            Substitute.For<IMediaService>(),
            Substitute.For<IUsersService>(),
            Substitute.For<IVenuesService>(),
            Substitute.For<IInteractionDispatcher>(),
            Substitute.For<IDownloadsService>(),
            Substitute.For<IServiceProvider>(),
            new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    [Fact]
    public async Task SearchAsync_AnswersAnEmptyPage_ForAnyQuery()
    {
        await _database.SeedAsync(Performance(1), Performance(2));

        var result = await _service.SearchAsync("anything", 1, 10);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_ListsEveryPerformance_WhenTheQueryIsBlank()
    {
        await _database.SeedAsync(Performance(1), Performance(2));

        var result = await _service.SearchAsync("   ", 1, 10);

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_WithABlankQuery_ListsInTheGivenOrder()
    {
        // Position 1 is the older, so the default (newest first) and a queuePosition sort disagree.
        await _database.SeedAsync(Performance(1, minutesAgo: 2), Performance(2, minutesAgo: 1));

        var result = await _service.SearchAsync("", 1, 10, new SortDescriptor("queuePosition", Descending: false));

        Assert.Equal([1, 2], result.Items.Select(p => p.QueuePosition));
    }

    private static Performance Performance(int position, int minutesAgo = 0) => new()
    {
        SingerId = Guid.NewGuid(),
        MediaId = Guid.NewGuid(),
        QueuePosition = position,
        CreatedDate = DateTime.UtcNow.AddMinutes(-minutesAgo),
    };

    public void Dispose() => _database.Dispose();
}
