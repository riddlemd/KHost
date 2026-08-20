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
