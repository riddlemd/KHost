using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

public class MediaRepositoryDeleteTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IDbContextFactory<DefaultContext> _factory;
    private readonly MediaRepository _repository;

    public MediaRepositoryDeleteTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"khost-del-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddDbContextFactory<DefaultContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}")
                   .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        var provider = services.BuildServiceProvider();
        _factory = provider.GetRequiredService<IDbContextFactory<DefaultContext>>();

        using var context = _factory.CreateDbContext();
        context.Database.Migrate();

        _repository = new MediaRepository(_factory, NullLogger<BaseRepository<Media>>.Instance);
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

    [Fact]
    public async Task DeleteAsync_RemovesTheRow()
    {
        var media = await SeedAsync();

        var deleted = await _repository.DeleteAsync(media.Id);

        Assert.True(deleted);
        Assert.Null(await _repository.ReadAsync(media.Id));
    }

    [Fact]
    public async Task DeleteAsync_AlsoClearsTheFtsIndex()
    {
        var media = await SeedAsync();

        await _repository.DeleteAsync(media.Id);

        var result = await _repository.SearchAsync("bohemian");
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_ForUnknownId()
    {
        Assert.False(await _repository.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteAsync_Succeeds_WhenAPerformanceReferencesTheMedia()
    {
        var media = await SeedAsync();

        using (var context = await _factory.CreateDbContextAsync())
        {
            var user = new KHostUser { Id = Guid.NewGuid(), Name = "Singer" };
            context.Users.Add(user);
            context.Performances.Add(new Performance
            {
                Id = Guid.NewGuid(),
                SingerId = user.Id,
                MediaId = media.Id,
            });
            await context.SaveChangesAsync();
        }

        // Performances index MediaId but declare no foreign key, so this must not be blocked.
        Assert.True(await _repository.DeleteAsync(media.Id));
    }

    private async Task<Media> SeedAsync()
    {
        var media = new Media
        {
            Title = "Bohemian Rhapsody",
            Artist = "Queen",
            FilePath = $"/library/{Guid.NewGuid():N}.mp4",
            Format = "mp4",
            Status = MediaStatus.Ready,
        };

        using var context = await _factory.CreateDbContextAsync();
        context.Media.Add(media);
        await context.SaveChangesAsync();

        return media;
    }
}
