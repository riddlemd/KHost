using KHost.Abstractions.Models;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

public class MediaRepositoryFingerprintTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IDbContextFactory<DefaultContext> _factory;
    private readonly MediaRepository _repository;

    public MediaRepositoryFingerprintTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"khost-fingerprint-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddDbContextFactory<DefaultContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}")
                   .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        _factory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<DefaultContext>>();

        using var context = _factory.CreateDbContext();
        context.Database.Migrate();

        _repository = new MediaRepository(_factory, NullLogger<BaseRepository<Media>>.Instance);
    }

    [Fact]
    public async Task GetByFileSizesAsync_ReturnsOnlyRowsMatchingOneOfTheSizes()
    {
        Seed(("/a.mp4", 100), ("/b.mp4", 200), ("/c.mp4", 300));

        var found = await _repository.GetByFileSizesAsync([100, 300]);

        Assert.Equal(["/a.mp4", "/c.mp4"], found.Select(m => m.FilePath).OrderBy(p => p));
    }

    [Fact]
    public async Task GetByFileSizesAsync_ReturnsEmpty_ForAnEmptySizeList()
    {
        Seed(("/a.mp4", 100));

        Assert.Empty(await _repository.GetByFileSizesAsync([]));
    }

    [Fact]
    public async Task GetByFileSizesAsync_IgnoresUnmeasuredRows()
    {
        Seed(("/measured.mp4", 100), ("/unmeasured.mp4", null));

        var found = await _repository.GetByFileSizesAsync([100]);

        Assert.Equal("/measured.mp4", Assert.Single(found).FilePath);
    }

    [Fact]
    public async Task GetWithoutFileSizeAsync_ReturnsOnlyUnmeasuredRows()
    {
        Seed(("/measured.mp4", 100), ("/unmeasured.mp4", null));

        var found = await _repository.GetWithoutFileSizeAsync();

        Assert.Equal("/unmeasured.mp4", Assert.Single(found).FilePath);
    }

    [Fact]
    public async Task UpdateFingerprintsAsync_PersistsSizeAndBothHashes()
    {
        Seed(("/a.mp4", null));
        var row = Assert.Single(await _repository.GetWithoutFileSizeAsync());

        row.FileSize = 4242;
        row.SampledHash = "sampled";
        row.ContentHash = "full";
        await _repository.UpdateFingerprintsAsync([row]);

        var stored = Assert.Single(await _repository.GetByFileSizesAsync([4242]));
        Assert.Equal(4242, stored.FileSize);
        Assert.Equal("sampled", stored.SampledHash);
        Assert.Equal("full", stored.ContentHash);
    }

    [Fact]
    public async Task UpdateFingerprintsAsync_LeavesEveryOtherColumnAlone()
    {
        Seed(("/a.mp4", 100));
        var row = Assert.Single(await _repository.GetByFileSizesAsync([100]));

        // The context is NoTracking, so a whole-entity update would write these back too.
        row.Title = "Clobbered";
        row.Artist = "Clobbered";
        row.Notes = "Clobbered";
        row.SampledHash = "sampled";
        await _repository.UpdateFingerprintsAsync([row]);

        var stored = Assert.Single(await _repository.GetByFileSizesAsync([100]));
        Assert.Equal("Title", stored.Title);
        Assert.Equal("Artist", stored.Artist);
        Assert.Equal("Notes", stored.Notes);
        Assert.Equal("sampled", stored.SampledHash);
    }

    [Fact]
    public async Task UpdateFingerprintsAsync_DoesNothing_ForAnEmptySet()
    {
        await _repository.UpdateFingerprintsAsync([]);

        Assert.Empty(await _repository.GetWithoutFileSizeAsync());
    }

    private void Seed(params (string Path, long? Size)[] rows)
    {
        using var context = _factory.CreateDbContext();
        foreach (var (path, size) in rows)
        {
            context.Media.Add(new Media
            {
                FilePath = path,
                Title = "Title",
                Artist = "Artist",
                Notes = "Notes",
                FileSize = size,
            });
        }

        context.SaveChanges();
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch
        {
        }

        GC.SuppressFinalize(this);
    }
}
