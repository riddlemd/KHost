using KHost.Abstractions.Models;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

public class PerformancesRepositoryNextQueuePositionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IDbContextFactory<DefaultContext> _factory;
    private readonly PerformancesRepository _repository;

    public PerformancesRepositoryNextQueuePositionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"khost-nextpos-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddDbContextFactory<DefaultContext>(options => options.UseSqlite($"Data Source={_dbPath}"));
        _factory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<DefaultContext>>();

        using var context = _factory.CreateDbContext();
        context.Database.Migrate();

        _repository = new PerformancesRepository(_factory, NullLogger<BaseRepository<Performance>>.Instance);
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); }
        catch (IOException) { }

        GC.SuppressFinalize(this);
    }

    private void Seed(Guid singerId, params int?[] positions)
    {
        using var context = _factory.CreateDbContext();
        foreach (var position in positions)
            context.Set<Performance>().Add(new Performance { SingerId = singerId, MediaId = Guid.NewGuid(), QueuePosition = position });
        context.SaveChanges();
    }

    [Fact]
    public async Task ReadNextQueuePositionForSingerAsync_ReturnsOne_WhenSingerHasNoPerformances()
    {
        Seed(Guid.NewGuid(), 7);

        Assert.Equal(1, await _repository.ReadNextQueuePositionForSingerAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ReadNextQueuePositionForSingerAsync_ReturnsOne_WhenSingerHasOnlyUnqueuedPerformances()
    {
        var singer = Guid.NewGuid();
        Seed(singer, null, null);

        Assert.Equal(1, await _repository.ReadNextQueuePositionForSingerAsync(singer));
    }

    [Fact]
    public async Task ReadNextQueuePositionForSingerAsync_ReturnsMaxPlusOne_ForThatSingerOnly()
    {
        var singer = Guid.NewGuid();
        Seed(singer, 2, 5, null, 3);
        Seed(Guid.NewGuid(), 9);

        Assert.Equal(6, await _repository.ReadNextQueuePositionForSingerAsync(singer));
    }
}
