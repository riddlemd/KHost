using KHost.Abstractions.Models;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

public class MediaRepositoryPathDedupTests : IDisposable
{
    // Windows and default macOS volumes treat paths case-insensitively; Linux does not, and the
    // repository matches that, so the expected result flips per platform.
    private static bool FoldsCase => !OperatingSystem.IsLinux();

    private readonly string _dbPath;
    private readonly IDbContextFactory<DefaultContext> _factory;
    private readonly MediaRepository _repository;

    public MediaRepositoryPathDedupTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"khost-dedup-{Guid.NewGuid():N}.db");

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
    public async Task GetExistingFilePathsAsync_ReturnsEmpty_WhenInputIsEmpty()
    {
        await SeedAsync(MakeMedia("/library/song.mp4"));

        var result = await _repository.GetExistingFilePathsAsync([]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetExistingFilePathsAsync_FindsExactPath()
    {
        await SeedAsync(MakeMedia("/library/song.mp4"));

        var result = await _repository.GetExistingFilePathsAsync(["/library/song.mp4"]);

        Assert.Contains("/library/song.mp4", result);
    }

    [Fact]
    public async Task GetExistingFilePathsAsync_ReturnsOnlyRequestedPaths()
    {
        await SeedAsync(
            MakeMedia("/library/a.mp4"),
            MakeMedia("/library/b.mp4"),
            MakeMedia("/library/c.mp4"));

        var result = await _repository.GetExistingFilePathsAsync(["/library/b.mp4"]);

        Assert.Single(result);
        Assert.Contains("/library/b.mp4", result);
    }

    [Fact]
    public async Task GetExistingFilePathsAsync_FindsAsciiCaseVariant_WhenPlatformFoldsCase()
    {
        await SeedAsync(MakeMedia("/library/song.mp4"));

        var result = await _repository.GetExistingFilePathsAsync(["/library/SONG.mp4"]);

        Assert.Equal(FoldsCase, result.Count == 1);
    }

    [Fact]
    public async Task GetExistingFilePathsAsync_FindsNonAsciiCaseVariant_WhenStoredPathIsUppercase()
    {
        // The stored side must hold the uppercase non-ASCII char for the defect to show: SQLite's
        // lower() folds ASCII only, so it leaves Ö alone while .NET folds it to ö, the row is
        // missed, the file re-imports, and the unique index on FilePath rejects it.
        await SeedAsync(MakeMedia("/library/SÖNG.mp4"));

        var result = await _repository.GetExistingFilePathsAsync(["/library/söng.mp4"]);

        Assert.Equal(FoldsCase, result.Count == 1);
    }

    [Fact]
    public async Task GetExistingFilePathsAsync_FindsNonAsciiCaseVariant_WhenStoredPathIsLowercase()
    {
        // The mirror of the case above, which happens to work even with SQL-side folding because
        // lowering an already-lowercase ö is a no-op. Pinned so a fix can't regress one direction.
        await SeedAsync(MakeMedia("/library/söng.mp4"));

        var result = await _repository.GetExistingFilePathsAsync(["/library/SÖNG.mp4"]);

        Assert.Equal(FoldsCase, result.Count == 1);
    }

    [Fact]
    public async Task GetExistingFilePathsAsync_ReturnedSetMatchesTheCallersInputCasing()
    {
        // MediaImportService filters with existing.Contains(inputPath), so the returned set's
        // comparer — not just its contents — decides whether a file is treated as new.
        await SeedAsync(MakeMedia("/library/SÖNG.mp4"));

        var result = await _repository.GetExistingFilePathsAsync(["/library/söng.mp4"]);

        Assert.Equal(FoldsCase, result.Contains("/library/söng.mp4"));
    }

    private async Task SeedAsync(params Media[] media)
    {
        using var context = await _factory.CreateDbContextAsync();
        context.Media.AddRange(media);
        await context.SaveChangesAsync();
    }

    private static Media MakeMedia(string filePath) => new()
    {
        Title = Path.GetFileNameWithoutExtension(filePath),
        Artist = "Test Artist",
        FilePath = filePath,
        Format = "mp4",
        Status = MediaStatus.Ready,
    };
}
