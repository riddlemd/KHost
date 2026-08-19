using KHost.Abstractions.Models;
using KHost.DataAccess.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

public class PerformancesRepositorySearchTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly PerformancesRepository _repository;

    public PerformancesRepositorySearchTests()
        => _repository = new PerformancesRepository(_database, NullLogger<BaseRepository<Performance>>.Instance);

    [Fact]
    public async Task SearchAsync_ReturnsNothing_WhenGivenAQuery()
    {
        await _database.SeedAsync(Performance(), Performance());

        var result = await _repository.SearchAsync("anything", 1, 10);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEverything_WhenQueryIsBlank()
    {
        await _database.SeedAsync(Performance(), Performance());

        var result = await _repository.SearchAsync("   ", 1, 10);

        Assert.Equal(2, result.TotalCount);
    }

    private static Performance Performance()
        => new() { SingerId = Guid.NewGuid(), MediaId = Guid.NewGuid(), CreatedDate = DateTime.UtcNow };

    public void Dispose() => _database.Dispose();
}
