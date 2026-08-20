using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

public class MediaRepositoryFtsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IDbContextFactory<DefaultContext> _factory;
    private readonly MediaRepository _repository;

    public MediaRepositoryFtsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"khost-fts-{Guid.NewGuid():N}.db");

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
    public async Task SearchAsync_PartialTitleSubstring_ReturnsMatch()
    {
        await SeedDefaultLibraryAsync();

        var result = await _repository.SearchAsync("rhap");

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("Bohemian Rhapsody", result.Items[0].Title);
    }

    [Fact]
    public async Task SearchAsync_ArtistMatch_ReturnsMatch()
    {
        await SeedDefaultLibraryAsync();

        var result = await _repository.SearchAsync("queen");

        Assert.Single(result.Items);
        Assert.Equal("Queen", result.Items[0].Artist);
    }

    [Fact]
    public async Task SearchAsync_MultipleTokens_AppliesAndAcrossColumns()
    {
        await SeedDefaultLibraryAsync();

        var result = await _repository.SearchAsync("queen bohem");

        Assert.Single(result.Items);
        Assert.Equal("Bohemian Rhapsody", result.Items[0].Title);
    }

    [Fact]
    public async Task SearchAsync_ApostropheInQuery_DoesNotThrow()
    {
        await SeedDefaultLibraryAsync();

        var result = await _repository.SearchAsync("don't");

        Assert.Single(result.Items);
        Assert.Equal("Don't Stop Believin'", result.Items[0].Title);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsAllRowsOrderedByTitle()
    {
        await SeedDefaultLibraryAsync();

        var result = await _repository.SearchAsync("");

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal("Bohemian Rhapsody", result.Items[0].Title);
        Assert.Equal("Don't Stop Believin'", result.Items[1].Title);
        Assert.Equal("Sweet Caroline", result.Items[2].Title);
    }

    [Fact]
    public async Task SearchAsync_OnlyPunctuationQuery_FindsNothing()
    {
        await SeedDefaultLibraryAsync();

        var result = await _repository.SearchAsync("\"*()");

        // Stripping the metacharacters leaves nothing to match on. Handing back the whole library
        // reads as "everything matched" when in fact nothing was searched for.
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_StatusFilter_ExcludesNonMatchingStatuses()
    {
        await SeedDefaultLibraryAsync();
        await SeedAsync(MakeMedia("Killer Queen", "Queen", MediaStatus.Broken));

        var readyOnly = new HashSet<MediaStatus> { MediaStatus.Ready };
        var result = await _repository.SearchAsync("queen", pageNumber: 1, pageSize: 50, options: readyOnly);

        Assert.Single(result.Items);
        Assert.Equal("Bohemian Rhapsody", result.Items[0].Title);
    }

    [Fact]
    public async Task SearchAsync_NoMatch_ReturnsEmptyResult()
    {
        await SeedDefaultLibraryAsync();

        var result = await _repository.SearchAsync("zzzunmatched");

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task SearchAsync_DeletedRow_NoLongerReturned()
    {
        await SeedDefaultLibraryAsync();

        using (var ctx = await _factory.CreateDbContextAsync())
        {
            var rhapsody = ctx.Media.Single(m => m.Title == "Bohemian Rhapsody");
            ctx.Media.Remove(rhapsody);
            await ctx.SaveChangesAsync();
        }

        var result = await _repository.SearchAsync("rhap");

        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_UpdatedRow_ReflectsNewTitle()
    {
        await SeedDefaultLibraryAsync();

        using (var ctx = await _factory.CreateDbContextAsync())
        {
            var rhapsody = ctx.Media.Single(m => m.Title == "Bohemian Rhapsody");
            rhapsody.Title = "Killer Queen";
            ctx.Media.Update(rhapsody);
            await ctx.SaveChangesAsync();
        }

        var oldQuery = await _repository.SearchAsync("rhap");
        Assert.Equal(0, oldQuery.TotalCount);

        var newQuery = await _repository.SearchAsync("killer");
        Assert.Equal(1, newQuery.TotalCount);
        Assert.Equal("Killer Queen", newQuery.Items[0].Title);
    }

    private Task SeedDefaultLibraryAsync()
        => SeedAsync(
            MakeMedia("Bohemian Rhapsody", "Queen"),
            MakeMedia("Don't Stop Believin'", "Journey"),
            MakeMedia("Sweet Caroline", "Neil Diamond"));

    private async Task SeedAsync(params Media[] media)
    {
        using var context = await _factory.CreateDbContextAsync();
        context.Media.AddRange(media);
        await context.SaveChangesAsync();
    }

    private static Media MakeMedia(string title, string artist, MediaStatus status = MediaStatus.Ready) => new()
    {
        Title = title,
        Artist = artist,
        FilePath = $"/library/{Guid.NewGuid():N}.mp4",
        Format = "mp4",
        Status = status,
    };
}
