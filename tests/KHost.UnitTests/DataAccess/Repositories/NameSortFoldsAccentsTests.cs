using KHost.Abstractions.Models;
using KHost.DataAccess.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

/// <summary>Bundled SQLite's lower() folds ASCII only, so a lower()-sorted list puts every accented
/// name after "Zoe"; the folded column sorts them where a host expects.</summary>
public class NameSortFoldsAccentsTests : IDisposable
{
    private static readonly string[] Names = ["Zoe", "Ärger", "ábel"];
    private static readonly string[] Folded = ["ábel", "Ärger", "Zoe"];

    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Users_SortByName_InFoldedOrder()
    {
        var repository = new UsersRepository(_database, NullLogger<BaseRepository<KHostUser>>.Instance);
        foreach (var name in Names)
            await repository.CreateAsync(new KHostUser { Name = name });

        await AssertSortedAsync(repository, u => u.Name);
    }

    [Fact]
    public async Task Venues_SortByName_InFoldedOrder()
    {
        var repository = new VenuesRepository(_database, NullLogger<BaseRepository<Venue>>.Instance);
        foreach (var name in Names)
            await repository.CreateAsync(new Venue { Name = name });

        await AssertSortedAsync(repository, v => v.Name);
    }

    [Fact]
    public async Task MediaPools_SortByName_InFoldedOrder()
    {
        var repository = new MediaPoolRepository(_database, NullLogger<BaseRepository<MediaPool>>.Instance);
        foreach (var name in Names)
            await repository.CreateAsync(new MediaPool { Name = name });

        await AssertSortedAsync(repository, p => p.Name);
    }

    [Fact]
    public async Task UserGroups_SortByName_InFoldedOrder()
    {
        var repository = new UserGroupsRepository(_database, NullLogger<BaseRepository<KHostUserGroup>>.Instance);
        foreach (var name in Names)
            await repository.CreateAsync(new KHostUserGroup { Name = name });

        await AssertSortedAsync(repository, g => g.Name);
    }

    [Fact]
    public async Task VisualisationPlaylists_SortByName_InFoldedOrder()
    {
        var repository = new VisualisationPlaylistRepository(_database, NullLogger<BaseRepository<VisualisationPlaylist>>.Instance);
        foreach (var name in Names)
            await repository.CreateAsync(new VisualisationPlaylist { Name = name });

        await AssertSortedAsync(repository, p => p.Name);
    }

    // Both the explicit "name" key and the default sort, filtered to these rows: the schema seeds groups of its own.
    private static async Task AssertSortedAsync<T>(BaseRepository<T> repository, Func<T, string> name)
        where T : RepositoryModel
    {
        var byKey = await repository.ReadAllAsync(1, 0, new SortDescriptor("name"));
        var byDefault = await repository.ReadAllAsync(1, 0, sort: null);

        Assert.Equal(Folded, byKey.Items.Select(name).Where(Names.Contains));
        Assert.Equal(Folded, byDefault.Items.Select(name).Where(Names.Contains));
    }
}
