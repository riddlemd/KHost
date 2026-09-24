using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

/// <summary>Asserts <see cref="PerformancesRepository.ReadNextQueuePositionForSingerAsync"/> and
/// <see cref="PerformancesRepository.ReadSingersNextPerformanceAsync"/> dispose the context they
/// open, by proving the context a capturing factory handed out is unusable afterwards.</summary>
public class PerformancesRepositoryContextDisposalTests : IDisposable
{
    private readonly string _dbPath;
    private readonly CapturingContextFactory _factory;
    private readonly PerformancesRepository _repository;

    public PerformancesRepositoryContextDisposalTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"khost-disposal-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddDbContextFactory<DefaultContext>(options => options.UseSqlite($"Data Source={_dbPath}"));
        var provider = services.BuildServiceProvider();

        using (var context = provider.GetRequiredService<IDbContextFactory<DefaultContext>>().CreateDbContext())
            context.Database.Migrate();

        _factory = new CapturingContextFactory(provider.GetRequiredService<IDbContextFactory<DefaultContext>>());
        _repository = new PerformancesRepository(_factory, NullLogger<BaseRepository<KHost.Abstractions.Models.Performance>>.Instance);
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); }
        catch (IOException) { }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ReadNextQueuePositionForSingerAsync_DisposesItsContext()
    {
        await _repository.ReadNextQueuePositionForSingerAsync(Guid.NewGuid());

        var context = Assert.Single(_factory.Created);
        Assert.Throws<ObjectDisposedException>(() => context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ReadSingersNextPerformanceAsync_DisposesItsContext()
    {
        await _repository.ReadSingersNextPerformanceAsync(Guid.NewGuid());

        var context = Assert.Single(_factory.Created);
        Assert.Throws<ObjectDisposedException>(() => context.ChangeTracker.Entries());
    }

    private sealed class CapturingContextFactory(IDbContextFactory<DefaultContext> inner) : IDbContextFactory<DefaultContext>
    {
        public List<DefaultContext> Created { get; } = [];

        public DefaultContext CreateDbContext()
        {
            var context = inner.CreateDbContext();
            Created.Add(context);
            return context;
        }

        public Task<DefaultContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            var context = inner.CreateDbContext();
            Created.Add(context);
            return Task.FromResult(context);
        }
    }
}
