using KHost.Abstractions.Models;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

/// <summary>Runs against real SQLite: an untold-EF property round-trips in memory but not here.</summary>
public class MediaRepositorySourceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IDbContextFactory<DefaultContext> _factory;
    private readonly MediaRepository _repository;

    public MediaRepositorySourceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"khost-media-source-{Guid.NewGuid():N}.db");

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
    public async Task Source_SurvivesASaveAndARead()
    {
        var created = await _repository.CreateAsync(Row("/downloads/62794.khv", source: "KaraFun"));

        var read = await _repository.ReadAsync(created.Id);

        Assert.Equal("KaraFun", read?.Source);
    }

    /// <summary>A file found on the host's own disk claims no provider, and reads back that way.</summary>
    [Fact]
    public async Task Source_UnsetOnTheWayIn_ReadsBackEmptyRatherThanNull()
    {
        var created = await _repository.CreateAsync(Row("/karaoke/song.mp4"));

        var read = await _repository.ReadAsync(created.Id);

        Assert.Equal(string.Empty, read?.Source);
    }

    /// <summary>Two providers can deliver the same song, and the rows have to stay tellable apart.</summary>
    [Fact]
    public async Task Source_DifferentProvidersForTheSameTitle_AreKeptApart()
    {
        await _repository.CreateAsync(Row("/downloads/62794.khv", source: "KaraFun"));
        await _repository.CreateAsync(Row("/downloads/zGvUbMxD5xg.mp4", source: "YouTube"));
        await _repository.CreateAsync(Row("/karaoke/regulate.mp4"));

        using var context = _factory.CreateDbContext();
        var bySource = context.Media.ToList().GroupBy(m => m.Source).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(new Dictionary<string, int> { ["KaraFun"] = 1, ["YouTube"] = 1, [""] = 1 }, bySource);
    }

    /// <summary>Not folded into the haystack, or "youtube" would match all its downloads.</summary>
    [Fact]
    public async Task Source_IsNotPartOfWhatSearchFolds()
    {
        var created = await _repository.CreateAsync(Row("/downloads/song.mp4", source: "YouTube"));

        var read = await _repository.ReadAsync(created.Id);

        Assert.DoesNotContain("youtube", read!.SearchFolded, StringComparison.OrdinalIgnoreCase);
    }

    private static Media Row(string path, string? source = null) => new()
    {
        FilePath = path,
        Title = "Regulate",
        Artist = "Warren G",
        Format = "MP4",
        Source = source ?? string.Empty,
    };

    public void Dispose()
    {
        try { File.Delete(_dbPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
