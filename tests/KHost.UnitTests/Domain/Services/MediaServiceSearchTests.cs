using KHost.Abstractions.Models;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

/// <summary>The typed search overload, through the real repository.</summary>
/// <remarks>Migrated rather than EnsureCreated: the ranked path needs tables only the migrations
/// carry.</remarks>
public class MediaServiceSearchTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"khost-media-search-{Guid.NewGuid():N}.db");
    private readonly IDbContextFactory<DefaultContext> _factory;
    private readonly MediaService _service;

    public MediaServiceSearchTests()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<DefaultContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}")
                   .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        _factory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<DefaultContext>>();

        using (var context = _factory.CreateDbContext())
            context.Database.Migrate();

        _service = new MediaService(
            NullLogger<MediaService>.Instance,
            new MediaRepository(_factory, NullLogger<BaseRepository<Media>>.Instance),
            new MessageBroker(NullLogger<MessageBroker>.Instance),
            Substitute.For<IServiceProvider>());
    }

    // "Thunder" is long enough to be ranked and "Th" is not: the two take different paths, and
    // each must read null options as songs alone.
    [Theory]
    [InlineData("Thunder")]
    [InlineData("Th")]
    public async Task SearchAsync_WithNullOptions_AnswersSongsAlone(string query)
    {
        await SeedSongAndBedAsync();

        var result = await _service.SearchAsync(query, 1, 50, sort: null, options: null);

        Assert.Equal(MediaType.Karaoke, Assert.Single(result.Items).Type);
    }

    [Fact]
    public async Task SearchAsync_WithAllTypes_ReachesPastSongs()
    {
        await SeedSongAndBedAsync();

        var result = await _service.SearchAsync("Thunder", 1, 50, sort: null, MediaSearchOptions.AllTypes);

        Assert.Equal(2, result.TotalCount);
    }

    private async Task SeedSongAndBedAsync()
    {
        using var context = _factory.CreateDbContext();
        context.AddRange(
            new Media { Title = "Thunder Road", Artist = "Artist", FilePath = "/song.mp4", Type = MediaType.Karaoke, Status = MediaStatus.Ready },
            new Media { Title = "Thunder Bed", Artist = "Artist", FilePath = "/bed.mp3", Type = MediaType.Audio, Status = MediaStatus.Ready });
        await context.SaveChangesAsync();
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }
}
