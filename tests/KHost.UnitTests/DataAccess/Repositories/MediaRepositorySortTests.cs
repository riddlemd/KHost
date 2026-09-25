using KHost.Abstractions.Models;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

// Runs against real SQLite: the provider refuses to ORDER BY a TimeSpan, which EF InMemory never does.
public class MediaRepositorySortTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IDbContextFactory<DefaultContext> _factory;
    private readonly MediaRepository _repository;

    public MediaRepositorySortTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"khost-sort-{Guid.NewGuid():N}.db");

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

    [Theory]
    [InlineData("")]
    [InlineData("track")]
    public async Task SearchAsync_SortByDuration_OrdersByLengthWithUnknownFirst(string query)
    {
        await SeedAsync();

        var result = await _repository.SearchAsync(query, 1, 50, new SortDescriptor("duration"), MediaSearchOptions.AllTypes);

        Assert.Equal(["Track none", "Track short", "Track mid", "Track long"], result.Items.Select(m => m.Title));
    }

    [Theory]
    [InlineData("")]
    [InlineData("track")]
    public async Task SearchAsync_SortByDurationDescending_OrdersLongestFirst(string query)
    {
        await SeedAsync();

        var result = await _repository.SearchAsync(query, 1, 50, new SortDescriptor("duration", Descending: true), MediaSearchOptions.AllTypes);

        Assert.Equal(["Track long", "Track mid", "Track short", "Track none"], result.Items.Select(m => m.Title));
    }

    private async Task SeedAsync()
    {
        using var context = await _factory.CreateDbContextAsync();

        // Seeded out of order, and with a fractional second, so neither insertion order nor a
        // text comparison of the stored value can pass for the sort.
        context.Media.AddRange(
            NewMedia("Track mid", TimeSpan.FromSeconds(95.5)),
            NewMedia("Track long", TimeSpan.FromMinutes(12)),
            NewMedia("Track none", null),
            NewMedia("Track short", TimeSpan.FromSeconds(9)));

        await context.SaveChangesAsync();
    }

    private static Media NewMedia(string title, TimeSpan? duration) => new()
    {
        Title = title,
        Artist = "Artist",
        FilePath = $"/library/{title}.mp4",
        Format = "mp4",
        Status = MediaStatus.Ready,
        Duration = duration,
    };
}
