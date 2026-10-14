using KHost.Abstractions.Models;
using KHost.DataAccess.Repositories;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.UnitTests.DataAccess;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

/// <summary>The typed search overload, through the real repository.</summary>
public class UsersServiceSearchTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly UsersService _service;

    public UsersServiceSearchTests()
    {
        _service = new UsersService(
            NullLogger<UsersService>.Instance,
            new UsersRepository(_database, NullLogger<BaseRepository<KHostUser>>.Instance),
            new UserGroupsRepository(_database, NullLogger<BaseRepository<KHostUserGroup>>.Instance),
            new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    [Fact]
    public async Task SearchAsync_WithSingersOnly_LeavesOutMembersOfAnExcludedGroup()
    {
        await SeedSingerAndStafferAsync();

        var result = await _service.SearchAsync("Sam", 1, 50, sort: null, new UserSearchOptions { SingersOnly = true });

        Assert.Equal("Sam Singer", Assert.Single(result.Items).Name);
    }

    [Fact]
    public async Task SearchAsync_WithNullOptions_LeavesEveryoneIn()
    {
        await SeedSingerAndStafferAsync();

        var result = await _service.SearchAsync("Sam", 1, 50, sort: null, options: null);

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_WithOptions_StillAppliesTheSort()
    {
        await SeedSingerAndStafferAsync();

        var result = await _service.SearchAsync("Sam", 1, 50, new SortDescriptor("name", Descending: true),
            new UserSearchOptions());

        Assert.Equal(["Sam Staffer", "Sam Singer"], result.Items.Select(u => u.Name));
    }

    private async Task SeedSingerAndStafferAsync()
    {
        var staff = new KHostUserGroup { Name = "Staff", ExcludeFromSingerQueue = true };
        // One call: a group handed to a second context is untracked, so EF inserts it again.
        await _database.SeedAsync(
            staff,
            new KHostUser { Name = "Sam Singer" },
            new KHostUser { Name = "Sam Staffer", Groups = [staff] });
    }

    public void Dispose() => _database.Dispose();
}
