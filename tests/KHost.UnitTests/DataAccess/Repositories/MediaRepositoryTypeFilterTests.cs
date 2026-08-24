using KHost.Abstractions.Models;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

// Every read a host queues from must answer with karaoke alone: an ad offered as a singable song
// reaches the screen with a singer's name against it. The default is the narrow one, so a caller
// that passes no options gets songs rather than everything.
//
// Migrated rather than EnsureCreated because half of these exercise the FTS path, and media_fts
// and its triggers are raw SQL that only the migrations carry.
public class MediaRepositoryTypeFilterTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IDbContextFactory<DefaultContext> _factory;
    private readonly MediaRepository _repository;

    public MediaRepositoryTypeFilterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"khost-kind-{Guid.NewGuid():N}.db");

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

    private async Task SeedAsync(params Media[] media)
    {
        using var context = _factory.CreateDbContext();
        context.AddRange(media);
        await context.SaveChangesAsync();
    }

    private static Media Song(string title, MediaType type, MediaStatus status = MediaStatus.Ready) => new()
    {
        Id = Guid.NewGuid(),
        FilePath = $"/media/{Guid.NewGuid():N}.mp4",
        Title = title,
        Artist = "Tester",
        Format = "MP4",
        Status = status,
        Type = type,
    };

    // "Thunder" is three characters and up, so this and the AllTypes case below both take the
    // FTS branch rather than the substring fallback.
    private async Task SeedOneOfEachTypeAsync()
    {
        await SeedAsync(
            Song("Thunder Road", MediaType.Karaoke),
            Song("Thunder Bed", MediaType.Audio),
            Song("Thunder Deal", MediaType.Video));
    }

    [Fact]
    public async Task SearchAsync_WithoutOptions_ReturnsKaraokeOnly()
    {
        await SeedOneOfEachTypeAsync();

        var result = await _repository.SearchAsync("Thunder", 1, 50);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Thunder Road", Assert.Single(result.Items).Title);
    }

    [Fact]
    public async Task SearchAsync_WithAllTypes_ReturnsEveryType()
    {
        await SeedOneOfEachTypeAsync();

        var result = await _repository.SearchAsync("Thunder", 1, 50, sort: null, MediaSearchOptions.AllTypes);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(
            [MediaType.Karaoke, MediaType.Video, MediaType.Audio],
            result.Items.Select(m => m.Type).OrderBy(k => k));
    }

    [Fact]
    public async Task SearchAsync_WithBreakMusicKind_ExcludesSongsAndAds()
    {
        await SeedOneOfEachTypeAsync();

        var result = await _repository.SearchAsync("Thunder", 1, 50, sort: null,
            new MediaSearchOptions { Types = [MediaType.Audio] });

        Assert.Equal("Thunder Bed", Assert.Single(result.Items).Title);
    }

    // A query under three characters cannot go to the trigram index, so this drops to the
    // substring fallback — a separate branch that has to filter by type the same way.
    [Fact]
    public async Task SearchAsync_ShortQueryTakingTheFallback_ReturnsKaraokeOnly()
    {
        await SeedAsync(
            Song("Go", MediaType.Karaoke),
            Song("Go", MediaType.Video));

        var result = await _repository.SearchAsync("Go", 1, 50);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(MediaType.Karaoke, Assert.Single(result.Items).Type);
    }

    // Sort and options together: no base overload carries both, so the media repository builds
    // that path itself and it is the one most likely to silently drop one of them.
    [Fact]
    public async Task SearchAsync_ShortQueryWithSortAndAllTypes_AppliesBoth()
    {
        await SeedAsync(
            Song("Go B", MediaType.Karaoke),
            Song("Go A", MediaType.Video),
            Song("Go C", MediaType.Audio));

        var result = await _repository.SearchAsync("Go", 1, 50,
            new SortDescriptor("title", Descending: true), MediaSearchOptions.AllTypes);

        // Both halves asserted: the count proves the options survived, the order proves the sort
        // did. Asserting only the count passes even when the sort is dropped on the floor.
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(["Go C", "Go B", "Go A"], result.Items.Select(m => m.Title));
    }

    [Fact]
    public async Task ReadAllAsync_WithoutOptions_ReturnsKaraokeOnly()
    {
        await SeedOneOfEachTypeAsync();

        var result = await _repository.ReadAllAsync(1, 50, sort: null);

        Assert.Equal(MediaType.Karaoke, Assert.Single(result.Items).Type);
    }

    // The count is taken from the filtered query too: a total that counted ads would page the
    // library past its own last row.
    [Fact]
    public async Task ReadAllAsync_WithoutOptions_CountsKaraokeOnly()
    {
        await SeedOneOfEachTypeAsync();

        var result = await _repository.ReadAllAsync(1, 50, sort: null);

        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task ReadAllAsync_WithAllTypes_ReturnsEveryType()
    {
        await SeedOneOfEachTypeAsync();

        var result = await _repository.ReadAllAsync(1, 50, sort: null, MediaSearchOptions.AllTypes);

        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task HasAnyAsync_WithOnlyBreakMusicAndAds_ReturnsFalse()
    {
        await SeedAsync(
            Song("Thunder Bed", MediaType.Audio),
            Song("Thunder Deal", MediaType.Video));

        Assert.False(await _repository.HasAnyAsync());
    }

    [Fact]
    public async Task HasAnyAsync_WithOneSong_ReturnsTrue()
    {
        await SeedAsync(Song("Thunder Road", MediaType.Karaoke));

        Assert.True(await _repository.HasAnyAsync());
    }

    // The picker bug: a paged read filtered in memory drops everything past the first page, so a
    // card sitting at row 51 of a real library is simply never offered.
    [Fact]
    public async Task ReadAllByTypesAsync_ReturnsEveryRow_PastTheFirstPage()
    {
        for (var i = 0; i < 60; i++)
            await SeedAsync(Song($"Song {i:D3}", MediaType.Karaoke));

        await SeedAsync(Song("Zebra Card", MediaType.Video));

        var ads = await _repository.ReadAllByTypesAsync(MediaType.Video);

        Assert.Equal("Zebra Card", Assert.Single(ads).Title);
    }

    [Fact]
    public async Task ReadAllByTypesAsync_SpansSeveralTypes()
    {
        await SeedOneOfEachTypeAsync();

        var sound = await _repository.ReadAllByTypesAsync(MediaType.Video, MediaType.Audio);

        Assert.Equal(2, sound.Count);
        Assert.DoesNotContain(sound, m => m.Type == MediaType.Karaoke);
    }

    [Fact]
    public async Task ReadAllByTypesAsync_NoTypes_ReturnsNothing()
    {
        await SeedOneOfEachTypeAsync();

        Assert.Empty(await _repository.ReadAllByTypesAsync());
    }

    // Dedup deliberately spans every type: FilePath is unique across the table, so an ad already
    // imported has to be found before the same path is inserted again as a song.
    [Fact]
    public async Task GetExistingFilePathsAsync_FindsPathsOfEveryKind()
    {
        var ad = Song("Thunder Deal", MediaType.Video);
        await SeedAsync(ad);

        var found = await _repository.GetExistingFilePathsAsync([ad.FilePath]);

        Assert.Contains(ad.FilePath, found);
    }

    [Fact]
    public async Task FindByFilePathAsync_FindsANonKaraokeRow()
    {
        var bed = Song("Thunder Bed", MediaType.Audio);
        await SeedAsync(bed);

        var found = await _repository.FindByFilePathAsync(bed.FilePath);

        Assert.Equal(bed.Id, found?.Id);
    }

    // Status and type are independent conditions, not one narrowing the other.
    [Fact]
    public async Task SearchAsync_WithStatusesAndKind_AppliesBoth()
    {
        await SeedAsync(
            Song("Thunder Road", MediaType.Karaoke, MediaStatus.Ready),
            Song("Thunder Rain", MediaType.Karaoke, MediaStatus.Broken),
            Song("Thunder Deal", MediaType.Video, MediaStatus.Ready));

        var result = await _repository.SearchAsync("Thunder", 1, 50, sort: null, new MediaSearchOptions
        {
            Types = [MediaType.Karaoke],
            Statuses = [MediaStatus.Ready],
        });

        Assert.Equal("Thunder Road", Assert.Single(result.Items).Title);
    }
}
