using KHost.Abstractions.Models;
using KHost.DataAccess.Repositories;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.UnitTests.DataAccess;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

/// <summary>Saves a user through the real repositories, where a stale snapshot costs real rows.</summary>
public class UsersServiceStoreTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly UsersRepository _users;
    private readonly UserGroupsRepository _groups;
    private readonly UsersService _service;

    public UsersServiceStoreTests()
    {
        _users = new UsersRepository(_database, NullLogger<BaseRepository<KHostUser>>.Instance);
        _groups = new UserGroupsRepository(_database, NullLogger<BaseRepository<KHostUserGroup>>.Instance);
        _service = new UsersService(
            NullLogger<UsersService>.Instance, _users, _groups, new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    /// <summary>A guest picks a song while the host has the singer's profile open.</summary>
    [Fact]
    public async Task UpdateAsync_AUserReadBeforeAKeyWasAdded_KeepsThatKey()
    {
        var ada = new KHostUser { Name = "Ada" };
        await _database.SeedAsync(ada);
        await _service.AddForeignKeyAsync(ada.Id, "Example", "account-1");

        var snapshot = (await _service.ReadAsync(ada.Id))!;
        await _service.AddForeignKeyAsync(ada.Id, "Example", "guest-7", isEphemeral: true);

        snapshot.Notes = "regular";
        await _service.UpdateAsync(snapshot);

        var stored = (await _service.ReadAsync(ada.Id))!;
        Assert.Equal("regular", stored.Notes);
        Assert.Equal(["account-1", "guest-7"], stored.ForeignKeys.Select(k => k.Key).Order().ToArray());
    }

    /// <summary>A guest leaves while the profile is open; the save must not tie them back on.</summary>
    [Fact]
    public async Task UpdateAsync_AUserReadBeforeAKeyWasRemoved_DoesNotBringItBack()
    {
        var ada = new KHostUser { Name = "Ada" };
        await _database.SeedAsync(ada);
        await _service.AddForeignKeyAsync(ada.Id, "Example", "guest-7", isEphemeral: true);

        var snapshot = (await _service.ReadAsync(ada.Id))!;
        await _service.RemoveForeignKeyAsync(ada.Id, "Example", "guest-7");

        await _service.UpdateAsync(snapshot);

        Assert.Empty((await _service.ReadAsync(ada.Id))!.ForeignKeys);
    }

    [Fact]
    public async Task UpdateAsync_ChangedGroups_AddsAndRemovesMembership()
    {
        var hosts = new KHostUserGroup { Name = "Hosts" };
        var regulars = new KHostUserGroup { Name = "Regulars" };
        var ada = new KHostUser { Name = "Ada" };
        await _database.SeedAsync(hosts, regulars, ada);
        await _groups.AddUserToGroupAsync(ada.Id, hosts.Id);

        var editing = (await _service.ReadAsync(ada.Id))!;
        editing.Groups = [regulars];
        await _service.UpdateAsync(editing);

        Assert.Equal([regulars.Id], (await _service.ReadAsync(ada.Id))!.Groups.Select(g => g.Id).ToArray());
    }

    /// <summary>Saving what a name lookup returned replaces memberships with the entity's.</summary>
    [Fact]
    public async Task UpdateAsync_AUserFoundByName_KeepsItsGroups()
    {
        var hosts = new KHostUserGroup { Name = "Hosts" };
        var ada = new KHostUser { Name = "Ada" };
        await _database.SeedAsync(hosts, ada);
        await _groups.AddUserToGroupAsync(ada.Id, hosts.Id);

        var found = (await _service.FindByNameAsync("ada"))!;
        found.Notes = "renamed nothing";
        await _service.UpdateAsync(found);

        Assert.Equal([hosts.Id], (await _service.ReadAsync(ada.Id))!.Groups.Select(g => g.Id).ToArray());
    }

    public void Dispose() => _database.Dispose();
}
