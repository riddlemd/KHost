using KHost.Abstractions.Models;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

/// <summary>
/// Free-text search over a real SQLite file. The bundled engine folds ASCII only, so these cases
/// only mean anything against it — an in-memory provider or the system sqlite3 CLI would pass even
/// with the matching pushed back into SQL.
/// </summary>
public class SearchCaseAndFallbackTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IDbContextFactory<DefaultContext> _factory;

    public SearchCaseAndFallbackTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"khost-search-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddDbContextFactory<DefaultContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}")
                   .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        _factory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<DefaultContext>>();

        using var context = _factory.CreateDbContext();
        context.Database.Migrate();
    }

    [Fact]
    public async Task VenueSearch_FindsAStoredUppercaseNonAsciiName()
    {
        Seed(context =>
        {
            context.Venues.Add(new Venue { Name = "ZOË'S BAR" });
            context.Venues.Add(new Venue { Name = "SOMEWHERE ELSE" });
        });
        var repository = new VenuesRepository(_factory, NullLogger<BaseRepository<Venue>>.Instance);

        var result = await repository.SearchAsync("zoë");

        Assert.Equal("ZOË'S BAR", Assert.Single(result.Items).Name);
    }

    [Fact]
    public async Task UserSearch_FindsAStoredUppercaseNonAsciiName()
    {
        Seed(context =>
        {
            context.Users.Add(new KHostUser { Name = "BJÖRK" });
            context.Users.Add(new KHostUser { Name = "Someone Else" });
        });
        var repository = new UsersRepository(_factory, NullLogger<BaseRepository<KHostUser>>.Instance);

        var result = await repository.SearchAsync("björk");

        Assert.Equal("BJÖRK", Assert.Single(result.Items).Name);
    }

    [Fact]
    public async Task UserGroupSearch_FindsAStoredUppercaseNonAsciiName()
    {
        Seed(context =>
        {
            context.UserGroups.Add(new KHostUserGroup { Name = "CRÈME" });
            context.UserGroups.Add(new KHostUserGroup { Name = "Something Else" });
        });
        var repository = new UserGroupsRepository(_factory, NullLogger<BaseRepository<KHostUserGroup>>.Instance);

        var result = await repository.SearchAsync("crème");

        Assert.Equal("CRÈME", Assert.Single(result.Items).Name);
    }

    [Fact]
    public async Task TipSearch_FindsStoredUppercaseNonAsciiNotes()
    {
        // Tip.UserId is the one enforced foreign key on this table, so the singer has to exist.
        var singer = new KHostUser { Name = "Singer" };
        Seed(context =>
        {
            context.Users.Add(singer);
            context.Tips.Add(new Tip { UserId = singer.Id, Amount = 5, Notes = "FOR RENÉE" });
            context.Tips.Add(new Tip { UserId = singer.Id, Amount = 5, Notes = "unrelated note" });
        });
        var repository = new TipsRepository(_factory, NullLogger<BaseRepository<Tip>>.Instance);

        var result = await repository.SearchAsync("renée");

        Assert.Equal("FOR RENÉE", Assert.Single(result.Items).Notes);
    }

    [Fact]
    public async Task VenueSearch_StillExcludesRowsThatDoNotMatch()
    {
        Seed(context =>
        {
            context.Venues.Add(new Venue { Name = "ZOË'S BAR" });
            context.Venues.Add(new Venue { Name = "SOMEWHERE ELSE" });
        });
        var repository = new VenuesRepository(_factory, NullLogger<BaseRepository<Venue>>.Instance);

        var result = await repository.SearchAsync("zoë");

        Assert.Single(result.Items);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task MediaSearch_FindsAShortQueryTheTrigramIndexCannotHold()
    {
        SeedMedia(("Plain Song", "Someone"), ("Nothing Here", "Nobody"));
        var repository = new MediaRepository(_factory, NullLogger<BaseRepository<Media>>.Instance);

        var result = await repository.SearchAsync("so");

        Assert.Equal("Plain Song", Assert.Single(result.Items).Title);
    }

    [Fact]
    public async Task MediaSearch_FindsASingleCharacterQuery()
    {
        SeedMedia(("Zebra", "Someone"), ("Nothing Here", "Nobody"));
        var repository = new MediaRepository(_factory, NullLogger<BaseRepository<Media>>.Instance);

        var result = await repository.SearchAsync("z");

        Assert.Equal("Zebra", Assert.Single(result.Items).Title);
    }

    [Fact]
    public async Task MediaSearch_DoesNotReturnTheWholeLibrary_ForAQueryOfOnlyPunctuation()
    {
        SeedMedia(("Plain Song", "Someone"), ("Another Song", "Someone Else"));
        var repository = new MediaRepository(_factory, NullLogger<BaseRepository<Media>>.Instance);

        var result = await repository.SearchAsync("***");

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task MediaSearch_StillUsesTheFullTextIndex_ForALongEnoughQuery()
    {
        SeedMedia(("Play That Funky Music", "Wild Cherry"), ("Colder Weather", "Zac Brown Band"));
        var repository = new MediaRepository(_factory, NullLogger<BaseRepository<Media>>.Instance);

        var result = await repository.SearchAsync("funky");

        Assert.Equal("Play That Funky Music", Assert.Single(result.Items).Title);
    }

    [Fact]
    public async Task MediaSearch_ShortQuery_MatchesTheArtistToo()
    {
        SeedMedia(("Plain Song", "Wild Cherry"), ("Nothing Here", "Nobody"));
        var repository = new MediaRepository(_factory, NullLogger<BaseRepository<Media>>.Instance);

        var result = await repository.SearchAsync("wi");

        Assert.Single(result.Items);
    }

    private void Seed(Action<DefaultContext> add)
    {
        using var context = _factory.CreateDbContext();
        add(context);
        context.SaveChanges();
    }

    private void SeedMedia(params (string Title, string Artist)[] rows)
    {
        using var context = _factory.CreateDbContext();
        var index = 0;
        foreach (var (title, artist) in rows)
        {
            context.Media.Add(new Media
            {
                FilePath = $"/library/{index++}.mp4",
                Title = title,
                Artist = artist,
                Status = MediaStatus.Ready,
            });
        }

        context.SaveChanges();
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
}
