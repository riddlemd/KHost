using KHost.Abstractions.Models;
using KHost.DataAccess.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

public class UsersRepositoryTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly UsersRepository _repository;

    public UsersRepositoryTests()
        => _repository = new UsersRepository(_database, NullLogger<BaseRepository<KHostUser>>.Instance);

    [Fact]
    public async Task FindByName_MatchesExactly()
    {
        await _database.SeedAsync(User("Steve"), User("Vaun"));

        Assert.Equal("Steve", (await _repository.FindByNameAsync("Steve"))?.Name);
        Assert.Null(await _repository.FindByNameAsync("steve"));
    }

    [Fact]
    public async Task HasAdminUser_IsFalse_WhenNobodyIsInAnAdminGroup()
    {
        await _database.SeedAsync(User("Steve"), new KHostUserGroup { Name = "Singers", IsAdmin = false });

        Assert.False(await _repository.HasAdminUserAsync());
    }

    [Fact]
    public async Task HasAdminUser_IsTrue_OnlyThroughGroupMembership()
    {
        var admins = new KHostUserGroup { Name = "Admins", IsAdmin = true };
        var steve = User("Steve");
        steve.Groups.Add(admins);

        await _database.SeedAsync(steve);

        Assert.True(await _repository.HasAdminUserAsync());
    }

    [Fact]
    public async Task Read_BringsTheGroupsWithIt_InNameOrder()
    {
        var steve = User("Steve");
        steve.Groups.Add(new KHostUserGroup { Name = "Zebras" });
        steve.Groups.Add(new KHostUserGroup { Name = "Antelopes" });
        await _database.SeedAsync(steve);

        var read = await _repository.ReadAsync(steve.Id);

        // Without the Include the groups come back empty and the user editor shows no memberships.
        Assert.NotNull(read);
        Assert.Equal(["Antelopes", "Zebras"], read.Groups.Select(g => g.Name));
    }

    [Fact]
    public async Task Search_MatchesPartOfAName_RegardlessOfCase()
    {
        await _database.SeedAsync(User("Steve"), User("Vaun"), User("mike"));

        var result = await _repository.SearchAsync("VE");

        Assert.Equal(["Steve"], result.Items.Select(u => u.Name));
    }

    [Fact]
    public async Task Search_WithNoQuery_ReturnsEveryoneSortedByName()
    {
        await _database.SeedAsync(User("Vaun"), User("mike"), User("Steve"));

        var result = await _repository.SearchAsync("");

        // Documents a trap rather than an intention: SQLite orders with binary collation, so every
        // lowercase name sorts after every uppercase one and "mike" lands last. Give the column a
        // NOCASE collation to change it — and change this test with it.
        Assert.Equal(["Steve", "Vaun", "mike"], result.Items.Select(u => u.Name));
    }

    [Fact]
    public async Task Search_DoesNotFoldAccents()
    {
        await _database.SeedAsync(User("Ándre"));

        // SQLite's lower() only folds ASCII, so the uppercase accent never matches. A host typing
        // the name as it appears finds nothing.
        Assert.Empty((await _repository.SearchAsync("ándre")).Items);
        Assert.Single((await _repository.SearchAsync("ndre")).Items);
    }

    [Fact]
    public async Task Search_CountsEveryMatch_NotJustThePageReturned()
    {
        for (var i = 0; i < 7; i++) await _database.SeedAsync(User($"Singer {i}"));

        var result = await _repository.SearchAsync("Singer", pageNumber: 1, pageSize: 3);

        // The count drives the pager, so taking it after paging would report one page of results.
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(7, result.TotalCount);
    }

    private static KHostUser User(string name) => new() { Name = name };

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }
}
