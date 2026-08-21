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
    public async Task FindByName_IgnoresCase()
    {
        await _database.SeedAsync(User("Steve"), User("Vaun"));

        Assert.Equal("Steve", (await _repository.FindByNameAsync("steve"))?.Name);
        Assert.Equal("Steve", (await _repository.FindByNameAsync("STEVE"))?.Name);
        Assert.Null(await _repository.FindByNameAsync("Stevie"));
    }

    [Fact]
    public async Task CreateAsync_RejectsANameDifferingOnlyInCase()
    {
        await _database.SeedAsync(User("Admin"));

        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(
            () => _repository.CreateAsync(User("admin")));
    }

    [Fact]
    public async Task CreateAsync_AllowsTwoDifferentNames()
    {
        await _database.SeedAsync(User("Alice"));

        await _repository.CreateAsync(User("Bob"));

        Assert.NotNull(await _repository.FindByNameAsync("alice"));
        Assert.NotNull(await _repository.FindByNameAsync("bob"));
    }

    [Fact]
    public async Task FindByName_IgnoresCase_OutsideAscii()
    {
        await _database.SeedAsync(User("BJÖRK"));

        Assert.Equal("BJÖRK", (await _repository.FindByNameAsync("björk"))?.Name);
        Assert.Equal("BJÖRK", (await _repository.FindByNameAsync("Björk"))?.Name);
    }

    [Fact]
    public async Task FindByName_MatchesAcrossUnicodeNormalisation()
    {
        // Composed on the way in, decomposed on the way back - what macOS hands out for the
        // same name read off a filesystem versus typed by hand.
        await _database.SeedAsync(User("Zo\u00EB"));

        Assert.NotNull(await _repository.FindByNameAsync("Zoe\u0308"));
    }

    [Fact]
    public async Task CreateAsync_RejectsANameDifferingOnlyInNormalisation()
    {
        await _database.SeedAsync(User("Zo\u00EB"));

        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(
            () => _repository.CreateAsync(User("Zoe\u0308")));
    }

    [Fact]
    public async Task FindByName_IgnoresAccents()
    {
        // Hosts on US keyboards cannot easily type accents a name was entered with.
        await _database.SeedAsync(User("Zoë"));

        Assert.NotNull(await _repository.FindByNameAsync("zoe"));
    }

    [Fact]
    public async Task CreateAsync_RejectsANameDifferingOnlyInAccents()
    {
        await _database.SeedAsync(User("Björk"));

        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(
            () => _repository.CreateAsync(User("Bjork")));
    }

    [Fact]
    public async Task CreateAsync_RejectsANameDifferingOnlyInCase_WhenTheDifferenceIsNotAscii()
    {
        await _database.SeedAsync(User("ZOË"));

        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(
            () => _repository.CreateAsync(User("zoë")));
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

        // Case-insensitively: SQLite orders with binary collation, so without lowering the sort
        // key "mike" would land below "Vaun" instead of at the top of the list.
        Assert.Equal(["mike", "Steve", "Vaun"], result.Items.Select(u => u.Name));
    }

    [Fact]
    public async Task Search_SortedByNameExplicitly_IgnoresCaseToo()
    {
        await _database.SeedAsync(User("Vaun"), User("mike"), User("Steve"));

        // The named column and the default have to agree, or the list reorders when a host clicks
        // the header it is already sorted by.
        var result = await _repository.SearchAsync("", 1, 10, new SortDescriptor("name", Descending: false));

        Assert.Equal(["mike", "Steve", "Vaun"], result.Items.Select(u => u.Name));
    }

    [Fact]
    public async Task Search_FoldsAccents()
    {
        await _database.SeedAsync(User("Ándre"));

        // Matching happens in .NET, so an uppercase accent folds the way the host expects rather
        // than the way SQLite's ASCII-only lower() would leave it.
        Assert.Single((await _repository.SearchAsync("ándre")).Items);
        Assert.Single((await _repository.SearchAsync("ÁNDRE")).Items);
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

    /// <summary>
    /// The flag lives on the group, so a singer who is also an admin has to be excluded by the
    /// group they share — not by anything on their own row.
    /// </summary>
    [Fact]
    public async Task Search_WithSingersOnly_LeavesOutMembersOfAnExcludedGroup()
    {
        var staff = new KHostUserGroup { Name = "Staff", ExcludeFromSingerQueue = true };
        var regulars = new KHostUserGroup { Name = "Regulars" };
        // One call: a group handed to a second context is untracked, so EF inserts it again and
        // trips the unique name index.
        await _database.SeedAsync(
            staff, regulars,
            new KHostUser { Name = "Sam Singer", Groups = [regulars] },
            new KHostUser { Name = "Sam Staffer", Groups = [staff] },
            new KHostUser { Name = "Sam Nobody" });

        var result = await _repository.SearchAsync("Sam", 1, 50, new UserSearchOptions { SingersOnly = true });

        Assert.Equal(["Sam Nobody", "Sam Singer"], result.Items.Select(u => u.Name).Order());
    }

    [Fact]
    public async Task Search_WithoutTheOption_LeavesEveryoneIn()
    {
        var staff = new KHostUserGroup { Name = "Staff", ExcludeFromSingerQueue = true };
        await _database.SeedAsync(
            staff,
            new KHostUser { Name = "Sam Singer" },
            new KHostUser { Name = "Sam Staffer", Groups = [staff] });

        var result = await _repository.SearchAsync("Sam", 1, 50);

        Assert.Equal(2, result.Items.Count);
    }

    private static KHostUser User(string name) => new() { Name = name };

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }
}
