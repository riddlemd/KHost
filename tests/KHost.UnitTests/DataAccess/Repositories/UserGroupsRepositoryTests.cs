using KHost.Abstractions.Models;
using KHost.DataAccess.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.DataAccess.Repositories;

/// <summary>
/// The user↔group join is a many-to-many through UserGroupMembership, so these run against real
/// SQLite: every caller elsewhere mocks the repository, which leaves the EF join query itself —
/// the part that breaks silently when the relationship is misconfigured — with nothing exercising
/// it. The Admin and Regular groups exist from the model's seed data once the schema is created.
/// </summary>
public class UserGroupsRepositoryTests : IDisposable
{
    private static readonly Guid Group = KHostUserGroup.AdminGroupId;

    private readonly SqliteTestDatabase _database = new();
    private readonly UserGroupsRepository _repository;

    public UserGroupsRepositoryTests()
        => _repository = new UserGroupsRepository(_database, NullLogger<BaseRepository<KHostUserGroup>>.Instance);

    [Fact]
    public async Task AddUserToGroupAsync_MakesTheUserAMember()
    {
        var user = User("Mike");
        await _database.SeedAsync(user);

        await _repository.AddUserToGroupAsync(user.Id, Group);

        Assert.True(await _repository.IsUserInGroupAsync(user.Id, Group));
    }

    [Fact]
    public async Task IsUserInGroupAsync_ANonMember_IsFalse()
    {
        var user = User("Mike");
        await _database.SeedAsync(user);

        Assert.False(await _repository.IsUserInGroupAsync(user.Id, Group));
    }

    [Fact]
    public async Task AddUserToGroupAsync_CalledTwice_IsIdempotent()
    {
        var user = User("Mike");
        await _database.SeedAsync(user);

        await _repository.AddUserToGroupAsync(user.Id, Group);
        // Without the exists-guard the second insert collides on the composite primary key.
        await _repository.AddUserToGroupAsync(user.Id, Group);

        Assert.Single(await _repository.GetAllUsersInGroupAsync(Group));
    }

    [Fact]
    public async Task RemoveUserFromGroupAsync_EndsTheMembership()
    {
        var user = User("Mike");
        await _database.SeedAsync(user);
        await _repository.AddUserToGroupAsync(user.Id, Group);

        await _repository.RemoveUserFromGroupAsync(user.Id, Group);

        Assert.False(await _repository.IsUserInGroupAsync(user.Id, Group));
    }

    [Fact]
    public async Task GetAllUsersInGroupAsync_ReturnsMembersAndNotOutsiders()
    {
        var member = User("Member");
        var outsider = User("Outsider");
        await _database.SeedAsync(member, outsider);
        await _repository.AddUserToGroupAsync(member.Id, Group);

        var users = await _repository.GetAllUsersInGroupAsync(Group);

        Assert.Equal("Member", Assert.Single(users).Name);
    }

    [Fact]
    public async Task GetAllUsersInGroupAsync_LoadsEachMembersGroups()
    {
        var user = User("Mike");
        await _database.SeedAsync(user);
        await _repository.AddUserToGroupAsync(user.Id, Group);

        var loaded = Assert.Single(await _repository.GetAllUsersInGroupAsync(Group));

        // The Include is the point: without it a returned user carries no groups at all.
        Assert.Contains(loaded.Groups, g => g.Id == Group);
    }

    private static KHostUser User(string name) => new() { Name = name };

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }
}
