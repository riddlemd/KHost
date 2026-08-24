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
public class MediaRepositoryKindFilterTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IDbContextFactory<DefaultContext> _factory;
    private readonly MediaRepository _repository;

    public MediaRepositoryKindFilterTests()
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

    private static Media Song(string title, MediaKind kind, MediaStatus status = MediaStatus.Ready) => new()
    {
        Id = Guid.NewGuid(),
        FilePath = $"/media/{Guid.NewGuid():N}.mp4",
        Title = title,
        Artist = "Tester",
        Format = "MP4",
        Status = status,
        Kind = kind,
    };

    // "Thunder" is three characters and up, so this and the AllKinds case below both take the
    // FTS branch rather than the substring fallback.
    private async Task SeedOneOfEachKindAsync()
    {
        await SeedAsync(
            Song("Thunder Road", MediaKind.Karaoke),
            Song("Thunder Bed", MediaKind.Audio),
            Song("Thunder Deal", MediaKind.Video));
    }

    [Fact]
    public async Task SearchAsync_WithoutOptions_ReturnsKaraokeOnly()
    {
        await SeedOneOfEachKindAsync();

        var result = await _repository.SearchAsync("Thunder", 1, 50);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Thunder Road", Assert.Single(result.Items).Title);
    }

    [Fact]
    public async Task SearchAsync_WithAllKinds_ReturnsEveryKind()
    {
        await SeedOneOfEachKindAsync();

        var result = await _repository.SearchAsync("Thunder", 1, 50, sort: null, MediaSearchOptions.AllKinds);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(
            [MediaKind.Karaoke, MediaKind.Video, MediaKind.Audio],
            result.Items.Select(m => m.Kind).OrderBy(k => k));
    }

    [Fact]
    public async Task SearchAsync_WithBreakMusicKind_ExcludesSongsAndAds()
    {
        await SeedOneOfEachKindAsync();

        var result = await _repository.SearchAsync("Thunder", 1, 50, sort: null,
            new MediaSearchOptions { Kind = MediaKind.Audio });

        Assert.Equal("Thunder Bed", Assert.Single(result.Items).Title);
    }

    // A query under three characters cannot go to the trigram index, so this drops to the
    // substring fallback — a separate branch that has to filter by kind the same way.
    [Fact]
    public async Task SearchAsync_ShortQueryTakingTheFallback_ReturnsKaraokeOnly()
    {
        await SeedAsync(
            Song("Go", MediaKind.Karaoke),
            Song("Go", MediaKind.Video));

        var result = await _repository.SearchAsync("Go", 1, 50);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(MediaKind.Karaoke, Assert.Single(result.Items).Kind);
    }

    // Sort and options together: no base overload carries both, so the media repository builds
    // that path itself and it is the one most likely to silently drop one of them.
    [Fact]
    public async Task SearchAsync_ShortQueryWithSortAndAllKinds_AppliesBoth()
    {
        await SeedAsync(
            Song("Go B", MediaKind.Karaoke),
            Song("Go A", MediaKind.Video),
            Song("Go C", MediaKind.Audio));

        var result = await _repository.SearchAsync("Go", 1, 50,
            new SortDescriptor("title", Descending: true), MediaSearchOptions.AllKinds);

        // Both halves asserted: the count proves the options survived, the order proves the sort
        // did. Asserting only the count passes even when the sort is dropped on the floor.
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(["Go C", "Go B", "Go A"], result.Items.Select(m => m.Title));
    }

    [Fact]
    public async Task ReadAllAsync_WithoutOptions_ReturnsKaraokeOnly()
    {
        await SeedOneOfEachKindAsync();

        var result = await _repository.ReadAllAsync(1, 50, sort: null);

        Assert.Equal(MediaKind.Karaoke, Assert.Single(result.Items).Kind);
    }

    // The count is taken from the filtered query too: a total that counted ads would page the
    // library past its own last row.
    [Fact]
    public async Task ReadAllAsync_WithoutOptions_CountsKaraokeOnly()
    {
        await SeedOneOfEachKindAsync();

        var result = await _repository.ReadAllAsync(1, 50, sort: null);

        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task ReadAllAsync_WithAllKinds_ReturnsEveryKind()
    {
        await SeedOneOfEachKindAsync();

        var result = await _repository.ReadAllAsync(1, 50, sort: null, MediaSearchOptions.AllKinds);

        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task HasAnyAsync_WithOnlyBreakMusicAndAds_ReturnsFalse()
    {
        await SeedAsync(
            Song("Thunder Bed", MediaKind.Audio),
            Song("Thunder Deal", MediaKind.Video));

        Assert.False(await _repository.HasAnyAsync());
    }

    [Fact]
    public async Task HasAnyAsync_WithOneSong_ReturnsTrue()
    {
        await SeedAsync(Song("Thunder Road", MediaKind.Karaoke));

        Assert.True(await _repository.HasAnyAsync());
    }

    // The picker bug: a paged read filtered in memory drops everything past the first page, so a
    // card sitting at row 51 of a real library is simply never offered.
    [Fact]
    public async Task ReadAllByKindsAsync_ReturnsEveryRow_PastTheFirstPage()
    {
        for (var i = 0; i < 60; i++)
            await SeedAsync(Song($"Song {i:D3}", MediaKind.Karaoke));

        await SeedAsync(Song("Zebra Card", MediaKind.Video));

        var ads = await _repository.ReadAllByKindsAsync(MediaKind.Video);

        Assert.Equal("Zebra Card", Assert.Single(ads).Title);
    }

    [Fact]
    public async Task ReadAllByKindsAsync_SpansSeveralKinds()
    {
        await SeedOneOfEachKindAsync();

        var sound = await _repository.ReadAllByKindsAsync(MediaKind.Video, MediaKind.Audio);

        Assert.Equal(2, sound.Count);
        Assert.DoesNotContain(sound, m => m.Kind == MediaKind.Karaoke);
    }

    [Fact]
    public async Task ReadAllByKindsAsync_NoKinds_ReturnsNothing()
    {
        await SeedOneOfEachKindAsync();

        Assert.Empty(await _repository.ReadAllByKindsAsync());
    }

    // Dedup deliberately spans every kind: FilePath is unique across the table, so an ad already
    // imported has to be found before the same path is inserted again as a song.
    [Fact]
    public async Task GetExistingFilePathsAsync_FindsPathsOfEveryKind()
    {
        var ad = Song("Thunder Deal", MediaKind.Video);
        await SeedAsync(ad);

        var found = await _repository.GetExistingFilePathsAsync([ad.FilePath]);

        Assert.Contains(ad.FilePath, found);
    }

    [Fact]
    public async Task FindByFilePathAsync_FindsANonKaraokeRow()
    {
        var bed = Song("Thunder Bed", MediaKind.Audio);
        await SeedAsync(bed);

        var found = await _repository.FindByFilePathAsync(bed.FilePath);

        Assert.Equal(bed.Id, found?.Id);
    }

    // Status and kind are independent conditions, not one narrowing the other.
    [Fact]
    public async Task SearchAsync_WithStatusesAndKind_AppliesBoth()
    {
        await SeedAsync(
            Song("Thunder Road", MediaKind.Karaoke, MediaStatus.Ready),
            Song("Thunder Rain", MediaKind.Karaoke, MediaStatus.Broken),
            Song("Thunder Deal", MediaKind.Video, MediaStatus.Ready));

        var result = await _repository.SearchAsync("Thunder", 1, 50, sort: null, new MediaSearchOptions
        {
            Kind = MediaKind.Karaoke,
            Statuses = [MediaStatus.Ready],
        });

        Assert.Equal("Thunder Road", Assert.Single(result.Items).Title);
    }
}
