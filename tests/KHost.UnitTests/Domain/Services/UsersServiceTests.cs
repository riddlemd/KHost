using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;

namespace KHost.UnitTests.Domain.Services;

public class UsersServiceTests
{
    private readonly ILogger<UsersService> _logger = Substitute.For<ILogger<UsersService>>();
    private readonly IUsersRepository _repository = Substitute.For<IUsersRepository>();
    private readonly IUserGroupsRepository _userGroupsRepository = Substitute.For<IUserGroupsRepository>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly UsersService _service;

    public UsersServiceTests()
    {
        _service = new UsersService(_logger, _repository, _userGroupsRepository, _broker);
    }

    [Fact]
    public async Task HasAdminUserAsync_ReturnsTrueWhenAdminExists()
    {
        _repository.HasAdminUserAsync().Returns(Task.FromResult(true));

        var result = await _service.HasAdminUserAsync();

        Assert.True(result);
        await _repository.Received(1).HasAdminUserAsync();
    }

    [Fact]
    public async Task HasAdminUserAsync_ReturnsFalseWhenNoAdminExists()
    {
        _repository.HasAdminUserAsync().Returns(Task.FromResult(false));

        var result = await _service.HasAdminUserAsync();

        Assert.False(result);
        await _repository.Received(1).HasAdminUserAsync();
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    [InlineData("00000000-0000-0000-0000-00000000009f")]
    public async Task DeleteAsync_RefusesToDeleteBuiltInUsers(string userId)
    {
        var result = await _service.DeleteAsync(new Guid(userId));

        Assert.False(result);
        await _repository.DidNotReceive().DeleteAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task DeleteAsync_DoesNotRaiseStateChangedForBuiltInUsers()
    {
        var notifications = 0;
        using var subscription = _broker.Subscribe<UsersChanged>(_ => notifications++);

        await _service.DeleteAsync(new Guid("00000000-0000-0000-0000-000000000001"));

        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task DeleteAsync_DeletesRegularUsers()
    {
        var userId = Guid.NewGuid();
        _repository.DeleteAsync(userId).Returns(Task.FromResult(true));

        var result = await _service.DeleteAsync(userId);

        Assert.True(result);
        await _repository.Received(1).DeleteAsync(userId);
    }

    [Fact]
    public async Task CreateAsync_AddsMembershipForEachSelectedGroup()
    {
        var hosts = new KHostUserGroup { Id = Guid.NewGuid(), Name = "Hosts" };
        var regulars = new KHostUserGroup { Id = Guid.NewGuid(), Name = "Regulars" };
        var user = new KHostUser { Name = "Dana", Groups = [hosts, regulars] };
        _repository.CreateAsync(user).Returns(Task.FromResult(user));

        await _service.CreateAsync(user);

        await _userGroupsRepository.Received(1).AddUserToGroupAsync(user.Id, hosts.Id);
        await _userGroupsRepository.Received(1).AddUserToGroupAsync(user.Id, regulars.Id);
    }

    [Fact]
    public async Task CreateAsync_SavesUserWithoutItsGroupsButRestoresThem()
    {
        var hosts = new KHostUserGroup { Id = Guid.NewGuid(), Name = "Hosts" };
        var user = new KHostUser { Name = "Dana", Groups = [hosts] };
        var groupsAtSave = new List<KHostUserGroup>();
        _repository.CreateAsync(user).Returns(_ =>
        {
            groupsAtSave = [.. user.Groups];
            return Task.FromResult(user);
        });

        var saved = await _service.CreateAsync(user);

        Assert.Empty(groupsAtSave);
        Assert.Equal([hosts], saved.Groups);
    }

    [Fact]
    public async Task UpdateAsync_AddsOnlyNewlySelectedGroups()
    {
        var hosts = new KHostUserGroup { Id = Guid.NewGuid(), Name = "Hosts" };
        var regulars = new KHostUserGroup { Id = Guid.NewGuid(), Name = "Regulars" };
        var userId = Guid.NewGuid();
        var stored = new KHostUser { Id = userId, Name = "Dana", Groups = [hosts] };
        _repository.ReadAsync(userId).Returns(Task.FromResult<KHostUser?>(stored));

        var edited = new KHostUser { Id = userId, Name = "Dana", Groups = [hosts, regulars] };
        await _service.UpdateAsync(edited);

        await _userGroupsRepository.Received(1).AddUserToGroupAsync(userId, regulars.Id);
        await _userGroupsRepository.DidNotReceive().AddUserToGroupAsync(userId, hosts.Id);
        await _userGroupsRepository.DidNotReceive().RemoveUserFromGroupAsync(userId, Arg.Any<Guid>());
    }

    [Fact]
    public async Task UpdateAsync_RemovesMembershipForDeselectedGroups()
    {
        var hosts = new KHostUserGroup { Id = Guid.NewGuid(), Name = "Hosts" };
        var regulars = new KHostUserGroup { Id = Guid.NewGuid(), Name = "Regulars" };
        var userId = Guid.NewGuid();
        var stored = new KHostUser { Id = userId, Name = "Dana", Groups = [hosts, regulars] };
        _repository.ReadAsync(userId).Returns(Task.FromResult<KHostUser?>(stored));

        var edited = new KHostUser { Id = userId, Name = "Dana", Groups = [regulars] };
        await _service.UpdateAsync(edited);

        await _userGroupsRepository.Received(1).RemoveUserFromGroupAsync(userId, hosts.Id);
        await _userGroupsRepository.DidNotReceive().AddUserToGroupAsync(userId, Arg.Any<Guid>());
    }

    [Fact]
    public async Task UpdateAsync_LeavesMembershipUntouchedWhenSelectionIsUnchanged()
    {
        var hosts = new KHostUserGroup { Id = Guid.NewGuid(), Name = "Hosts" };
        var userId = Guid.NewGuid();
        var stored = new KHostUser { Id = userId, Name = "Dana", Groups = [hosts] };
        _repository.ReadAsync(userId).Returns(Task.FromResult<KHostUser?>(stored));

        var edited = new KHostUser { Id = userId, Name = "Dana Renamed", Groups = [hosts] };
        await _service.UpdateAsync(edited);

        await _userGroupsRepository.DidNotReceive().AddUserToGroupAsync(userId, Arg.Any<Guid>());
        await _userGroupsRepository.DidNotReceive().RemoveUserFromGroupAsync(userId, Arg.Any<Guid>());
    }

    [Fact]
    public async Task UpdateAsync_SavesUserWithoutItsGroupsButRestoresThem()
    {
        var hosts = new KHostUserGroup { Id = Guid.NewGuid(), Name = "Hosts" };
        var userId = Guid.NewGuid();
        _repository.ReadAsync(userId).Returns(Task.FromResult<KHostUser?>(null));

        var edited = new KHostUser { Id = userId, Name = "Dana", Groups = [hosts] };
        var groupsAtSave = new List<KHostUserGroup>();
        _repository.UpdateAsync(edited).Returns(_ =>
        {
            groupsAtSave = [.. edited.Groups];
            return Task.CompletedTask;
        });

        await _service.UpdateAsync(edited);

        Assert.Empty(groupsAtSave);
        Assert.Equal([hosts], edited.Groups);
    }

    [Fact]
    public async Task CreateAsync_RaisesStateChangedOnlyAfterMembershipIsWritten()
    {
        var hosts = new KHostUserGroup { Id = Guid.NewGuid(), Name = "Hosts" };
        var user = new KHostUser { Name = "Dana", Groups = [hosts] };
        _repository.CreateAsync(user).Returns(Task.FromResult(user));

        var membershipWrites = 0;
        var writesWhenNotified = -1;
        _userGroupsRepository.AddUserToGroupAsync(Arg.Any<Guid>(), Arg.Any<Guid>())
            .Returns(_ => { membershipWrites++; return Task.CompletedTask; });
        using var subscription = _broker.Subscribe<UsersChanged>(_ => writesWhenNotified = membershipWrites);

        await _service.CreateAsync(user);

        Assert.Equal(1, writesWhenNotified);
    }

    [Fact]
    public async Task UpdateAsync_RaisesStateChangedOnlyAfterMembershipIsWritten()
    {
        var hosts = new KHostUserGroup { Id = Guid.NewGuid(), Name = "Hosts" };
        var userId = Guid.NewGuid();
        _repository.ReadAsync(userId).Returns(Task.FromResult<KHostUser?>(
            new KHostUser { Id = userId, Name = "Dana", Groups = [] }));

        var membershipWrites = 0;
        var writesWhenNotified = -1;
        _userGroupsRepository.AddUserToGroupAsync(Arg.Any<Guid>(), Arg.Any<Guid>())
            .Returns(_ => { membershipWrites++; return Task.CompletedTask; });
        using var subscription = _broker.Subscribe<UsersChanged>(_ => writesWhenNotified = membershipWrites);

        await _service.UpdateAsync(new KHostUser { Id = userId, Name = "Dana", Groups = [hosts] });

        Assert.Equal(1, writesWhenNotified);
    }
}
