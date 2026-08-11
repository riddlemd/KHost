using KHost.Abstractions.Models;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

public class PerformancesRepositoryCountSungSinceTests : IDisposable
{
    private static readonly DateTime Midnight = new(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath;
    private readonly IDbContextFactory<DefaultContext> _factory;
    private readonly PerformancesRepository _repository;

    public PerformancesRepositoryCountSungSinceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"khost-sung-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddDbContextFactory<DefaultContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}")
                   .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        var provider = services.BuildServiceProvider();
        _factory = provider.GetRequiredService<IDbContextFactory<DefaultContext>>();

        using var context = _factory.CreateDbContext();
        context.Database.Migrate();

        _repository = new PerformancesRepository(_factory, NullLogger<BaseRepository<Performance>>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private void Seed(params Performance[] performances)
    {
        using var context = _factory.CreateDbContext();
        context.Set<Performance>().AddRange(performances);
        context.SaveChanges();
    }

    private static Performance Sung(Guid singerId, DateTime on)
        => new() { SingerId = singerId, MediaId = Guid.NewGuid(), CreatedDate = on, QueuePosition = null };

    [Fact]
    public async Task CountSungSinceAsync_CountsPerSinger()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        Seed(
            Sung(alice, Midnight.AddHours(21)),
            Sung(alice, Midnight.AddHours(22)),
            Sung(bob, Midnight.AddHours(21)));

        var result = await _repository.CountSungSinceAsync([alice, bob], Midnight);

        Assert.Equal(2, result[alice]);
        Assert.Equal(1, result[bob]);
    }

    [Fact]
    public async Task CountSungSinceAsync_ExcludesPerformancesBeforeTheCutoff()
    {
        var alice = Guid.NewGuid();
        Seed(
            Sung(alice, Midnight.AddHours(-3)),
            Sung(alice, Midnight.AddHours(21)));

        var result = await _repository.CountSungSinceAsync([alice], Midnight);

        Assert.Equal(1, result[alice]);
    }

    // Still queued means not yet sung, so it must not count towards tonight's total.
    [Fact]
    public async Task CountSungSinceAsync_IgnoresStillQueuedPerformances()
    {
        var alice = Guid.NewGuid();
        Seed(
            Sung(alice, Midnight.AddHours(21)),
            new Performance
            {
                SingerId = alice,
                MediaId = Guid.NewGuid(),
                CreatedDate = Midnight.AddHours(22),
                QueuePosition = 0,
            });

        var result = await _repository.CountSungSinceAsync([alice], Midnight);

        Assert.Equal(1, result[alice]);
    }

    [Fact]
    public async Task CountSungSinceAsync_OmitsSingersWithNothingSince()
    {
        var alice = Guid.NewGuid();
        var quiet = Guid.NewGuid();
        Seed(Sung(alice, Midnight.AddHours(21)));

        var result = await _repository.CountSungSinceAsync([alice, quiet], Midnight);

        Assert.False(result.ContainsKey(quiet));
    }

    [Fact]
    public async Task CountSungSinceAsync_ExcludesSingersNotAskedFor()
    {
        var alice = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        Seed(
            Sung(alice, Midnight.AddHours(21)),
            Sung(stranger, Midnight.AddHours(21)));

        var result = await _repository.CountSungSinceAsync([alice], Midnight);

        Assert.Single(result);
        Assert.True(result.ContainsKey(alice));
    }

    [Fact]
    public async Task CountSungSinceAsync_ReturnsEmptyForNoSingers()
    {
        var result = await _repository.CountSungSinceAsync([], Midnight);

        Assert.Empty(result);
    }
}
