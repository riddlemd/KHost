using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

// Every repository read defaults to pageSize: 0, which PaginationComponent turns into the
// default page size. The result must report that effective size — reporting the raw 0 made
// PaginatedResult.TotalPages divide by zero for every caller using the defaults.
public class MediaRepositoryPaginationTests : IDisposable
{
    private const int BaseDefaultPageSize = 50;
    private const int BaseMaxPageSize = 1000;

    private readonly string _dbPath;
    private readonly IDbContextFactory<DefaultContext> _factory;
    private readonly MediaRepository _repository;

    public MediaRepositoryPaginationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"khost-paging-{Guid.NewGuid():N}.db");

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
    public async Task ReadAllAsync_WithDefaults_ReportsEffectivePageValues()
    {
        await SeedAsync(120);

        var result = await _repository.ReadAllAsync();

        Assert.Equal(BaseDefaultPageSize, result.PageSize);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(BaseDefaultPageSize, result.Items.Count);
        Assert.Equal(120, result.TotalCount);
    }

    [Fact]
    public async Task ReadAllAsync_WithDefaults_TotalPagesDoesNotThrow()
    {
        await SeedAsync(120);

        var result = await _repository.ReadAllAsync();

        Assert.Equal(3, result.TotalPages);
        Assert.True(result.HasNextPage);
        Assert.False(result.HasPreviousPage);
    }

    [Fact]
    public async Task ReadAllAsync_CapsPageSizeAtMax_AndReportsTheCap()
    {
        await SeedAsync(20);

        var result = await _repository.ReadAllAsync(pageNumber: 1, pageSize: 5000);

        Assert.Equal(BaseMaxPageSize, result.PageSize);
    }

    [Fact]
    public async Task SearchAsync_NonFtsPath_WithDefaults_ReportsEffectivePageSize()
    {
        await SeedAsync(120);

        // An empty query produces no FTS match expression, so this exercises the base pipeline.
        var result = await _repository.SearchAsync("");

        Assert.Equal(BaseDefaultPageSize, result.PageSize);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(3, result.TotalPages);
    }

    [Fact]
    public async Task SearchAsync_FtsPath_WithDefaults_ReportsEffectivePageSize()
    {
        await SeedAsync(120);

        var result = await _repository.SearchAsync("track");

        Assert.Equal(BaseDefaultPageSize, result.PageSize);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(120, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
    }

    [Fact]
    public async Task SearchAsync_FtsPath_CapsPageSizeAtMax_AndReportsTheCap()
    {
        await SeedAsync(20);

        var result = await _repository.SearchAsync("track", pageNumber: 1, pageSize: 5000);

        Assert.Equal(BaseMaxPageSize, result.PageSize);
    }

    private async Task SeedAsync(int count)
    {
        using var context = await _factory.CreateDbContextAsync();

        context.Media.AddRange(Enumerable.Range(1, count).Select(i => new Media
        {
            Title = $"Track {i}",
            Artist = $"Artist {i}",
            FilePath = $"/library/track-{i}.mp4",
            Format = "mp4",
            Status = MediaStatus.Ready,
        }));

        await context.SaveChangesAsync();
    }
}
