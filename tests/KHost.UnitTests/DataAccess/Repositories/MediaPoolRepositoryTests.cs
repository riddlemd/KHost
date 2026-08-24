using KHost.Abstractions.Models;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

public class MediaPoolRepositoryTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly MediaPoolRepository _repository;

    public MediaPoolRepositoryTests()
        => _repository = new MediaPoolRepository(_database, NullLogger<BaseRepository<MediaPool>>.Instance);

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<Media> SeedMediaAsync(string title, MediaType type)
    {
        var media = new Media
        {
            Id = Guid.NewGuid(),
            FilePath = $"/media/{Guid.NewGuid():N}.mp3",
            Title = title,
            Artist = "Tester",
            Format = "MP3",
            Status = MediaStatus.Ready,
            Type = type,
        };

        await _database.SeedAsync(media);
        return media;
    }

    private async Task<MediaPool> SeedPoolAsync(string name, PoolPurpose purpose = PoolPurpose.BreakMusic, Guid? venueId = null)
    {
        var pool = new MediaPool { Id = Guid.NewGuid(), Name = name, Purpose = purpose, VenueId = venueId };
        await _database.SeedAsync(pool);
        return pool;
    }

    [Fact]
    public async Task CreateAsync_FoldsTheName()
    {
        var created = await _repository.CreateAsync(new MediaPool { Name = "Björk Set" });

        var stored = await _repository.ReadAsync(created.Id);

        Assert.Equal("bjork set", stored?.NameFolded);
    }

    [Fact]
    public async Task ReadWithEntriesAsync_ReturnsEntriesInPositionOrder()
    {
        var pool = await SeedPoolAsync("Bed");
        var first = await SeedMediaAsync("First", MediaType.Audio);
        var second = await SeedMediaAsync("Second", MediaType.Audio);

        await _repository.ReplaceEntriesAsync(pool.Id,
        [
            new MediaPoolEntry { MediaId = second.Id },
            new MediaPoolEntry { MediaId = first.Id },
        ]);

        var loaded = await _repository.ReadWithEntriesAsync(pool.Id);

        Assert.Equal([second.Id, first.Id], loaded!.Entries.Select(e => e.MediaId));
        Assert.Equal([0, 1], loaded.Entries.Select(e => e.Position));
    }

    // The repository copies an entry field by field, so a column added to the model and not here
    // is dropped in silence — which is exactly how an ad lost its voiceover on every save.
    [Fact]
    public async Task ReplaceEntriesAsync_KeepsTheWholeComposition()
    {
        var pool = await SeedPoolAsync("Spots", PoolPurpose.Ads);
        var still = await SeedMediaAsync("Card", MediaType.Image);
        var voice = await SeedMediaAsync("Voiceover", MediaType.Audio);

        await _repository.ReplaceEntriesAsync(pool.Id,
        [
            new MediaPoolEntry
            {
                MediaId = still.Id,
                AudioMediaId = voice.Id,
                AudioStart = TimeSpan.FromSeconds(90),
                Duration = TimeSpan.FromSeconds(20),
                Weight = 7,
            },
        ]);

        var entry = Assert.Single((await _repository.ReadWithEntriesAsync(pool.Id))!.Entries);

        Assert.Equal(still.Id, entry.MediaId);
        Assert.Equal(voice.Id, entry.AudioMediaId);
        Assert.Equal(TimeSpan.FromSeconds(90), entry.AudioStart);
        Assert.Equal(TimeSpan.FromSeconds(20), entry.Duration);
        Assert.Equal(7, entry.Weight);
    }

    // Position comes from the order handed in, not from whatever the model carried: the editor
    // reorders by moving rows and only the resulting list knows the answer.
    [Fact]
    public async Task ReplaceEntriesAsync_AssignsPositionsFromTheGivenOrder()
    {
        var pool = await SeedPoolAsync("Bed");
        var media = await SeedMediaAsync("Track", MediaType.Audio);

        await _repository.ReplaceEntriesAsync(pool.Id,
        [
            new MediaPoolEntry { MediaId = media.Id, Position = 99 },
        ]);

        var loaded = await _repository.ReadWithEntriesAsync(pool.Id);

        Assert.Equal(0, Assert.Single(loaded!.Entries).Position);
    }

    [Fact]
    public async Task ReplaceEntriesAsync_RemovesTheEntriesItReplaced()
    {
        var pool = await SeedPoolAsync("Bed");
        var oldTrack = await SeedMediaAsync("Old", MediaType.Audio);
        var newTrack = await SeedMediaAsync("New", MediaType.Audio);

        await _repository.ReplaceEntriesAsync(pool.Id, [new MediaPoolEntry { MediaId = oldTrack.Id }]);
        await _repository.ReplaceEntriesAsync(pool.Id, [new MediaPoolEntry { MediaId = newTrack.Id }]);

        var loaded = await _repository.ReadWithEntriesAsync(pool.Id);

        Assert.Equal(newTrack.Id, Assert.Single(loaded!.Entries).MediaId);
    }

    // A pool with no venue belongs to every venue, so it has to come back alongside the ones the
    // venue owns rather than being filtered out with the other venues' pools.
    [Fact]
    public async Task ReadAllWithEntriesAsync_IncludesPoolsScopedToNoVenue()
    {
        var venueId = Guid.NewGuid();
        var otherVenueId = Guid.NewGuid();

        await SeedPoolAsync("Everywhere", venueId: null);
        await SeedPoolAsync("This venue", venueId: venueId);
        await SeedPoolAsync("Somewhere else", venueId: otherVenueId);

        var pools = await _repository.ReadAllWithEntriesAsync(PoolPurpose.BreakMusic, venueId);

        Assert.Equal(["Everywhere", "This venue"], pools.Select(p => p.Name).OrderBy(n => n));
    }

    [Fact]
    public async Task ReadAllWithEntriesAsync_ExcludesTheOtherKind()
    {
        await SeedPoolAsync("Bed", PoolPurpose.BreakMusic);
        await SeedPoolAsync("Spots", PoolPurpose.Ads);

        var pools = await _repository.ReadAllWithEntriesAsync(PoolPurpose.Ads, venueId: null);

        Assert.Equal("Spots", Assert.Single(pools).Name);
    }

    // Deleting a media row takes its pool lines with it; the alternative is an entry pointing at
    // nothing, which the selector would skip forever.
    [Fact]
    public async Task DeletingMedia_RemovesItsPoolEntries()
    {
        var pool = await SeedPoolAsync("Bed");
        var media = await SeedMediaAsync("Doomed", MediaType.Audio);

        await _repository.ReplaceEntriesAsync(pool.Id, [new MediaPoolEntry { MediaId = media.Id }]);

        using (var context = _database.CreateDbContext())
        {
            context.Media.Remove(await context.Media.FirstAsync(m => m.Id == media.Id));
            await context.SaveChangesAsync();
        }

        var loaded = await _repository.ReadWithEntriesAsync(pool.Id);

        Assert.Empty(loaded!.Entries);
    }

    [Fact]
    public async Task DeletingAPool_RemovesItsOwnEntries()
    {
        var pool = await SeedPoolAsync("Bed");
        var media = await SeedMediaAsync("Track", MediaType.Audio);

        await _repository.ReplaceEntriesAsync(pool.Id, [new MediaPoolEntry { MediaId = media.Id }]);
        await _repository.DeleteAsync(pool.Id);

        using var context = _database.CreateDbContext();

        Assert.Empty(await context.MediaPoolEntries.Where(e => e.MediaPoolId == pool.Id).ToListAsync());
    }

    [Fact]
    public async Task SearchAsync_WithKindOption_ReturnsThatKindOnly()
    {
        await SeedPoolAsync("House Bed", PoolPurpose.BreakMusic);
        await SeedPoolAsync("House Spots", PoolPurpose.Ads);

        var result = await _repository.SearchAsync("House", 1, 50,
            new MediaPoolSearchOptions { Purpose = PoolPurpose.Ads });

        Assert.Equal("House Spots", Assert.Single(result.Items).Name);
    }
}
