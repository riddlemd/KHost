using KHost.Abstractions.Models;
using KHost.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

public class VenuesRepositoryTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly VenuesRepository _repository;

    public VenuesRepositoryTests()
        => _repository = new VenuesRepository(_database, NullLogger<BaseRepository<Venue>>.Instance);

    [Fact]
    public async Task CreateAsync_RejectsADuplicateName()
    {
        await _repository.CreateAsync(new Venue { Name = "The Alley" });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => _repository.CreateAsync(new Venue { Name = "The Alley" }));
    }

    [Fact]
    public async Task CreateAsync_AllowsDistinctNames()
    {
        await _repository.CreateAsync(new Venue { Name = "The Alley" });
        await _repository.CreateAsync(new Venue { Name = "The Alley Annex" });

        Assert.True(await _repository.HasAnyAsync());
    }

    public void Dispose() => _database.Dispose();
}
