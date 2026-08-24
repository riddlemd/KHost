using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;

namespace KHost.UnitTests.Domain.Services;

public class UserGroupsServiceTests
{
    private readonly ILogger<UserGroupsService> _logger = Substitute.For<ILogger<UserGroupsService>>();
    private readonly IUserGroupsRepository _repository = Substitute.For<IUserGroupsRepository>();
    private readonly UserGroupsService _service;

    public UserGroupsServiceTests()
    {
        _service = new UserGroupsService(_logger, _repository, new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    [Fact]
    public async Task AddUserToGroupAsync_DelegatesToRepository()
    {
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        await _service.AddUserToGroupAsync(userId, groupId);

        await _repository.Received(1).AddUserToGroupAsync(userId, groupId);
    }

    [Fact]
    public async Task RemoveUserFromGroupAsync_DelegatesToRepository()
    {
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        await _service.RemoveUserFromGroupAsync(userId, groupId);

        await _repository.Received(1).RemoveUserFromGroupAsync(userId, groupId);
    }

    [Fact]
    public async Task IsUserInGroupAsync_DelegatesToRepository()
    {
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        _repository.IsUserInGroupAsync(userId, groupId).Returns(Task.FromResult(true));

        var result = await _service.IsUserInGroupAsync(userId, groupId);

        Assert.True(result);
        await _repository.Received(1).IsUserInGroupAsync(userId, groupId);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000001")] // Admin
    [InlineData("00000000-0000-0000-0000-000000000002")] // Regular
    [InlineData("00000000-0000-0000-0000-00000000009f")] // any future seeded group
    public async Task DeleteAsync_RefusesToDeleteBuiltInGroups(string groupId)
    {
        var result = await _service.DeleteAsync(new Guid(groupId));

        Assert.False(result);
        await _repository.DidNotReceive().DeleteAsync(Arg.Any<Guid>());
    }

    // Only the trailing node is free; a group sharing just the leading zeros is still deletable.
    [Fact]
    public async Task DeleteAsync_DeletesGroupsThatOnlyPartiallyMatchTheBuiltInPrefix()
    {
        var groupId = new Guid("00000000-0000-0000-0001-000000000001");
        _repository.DeleteAsync(groupId).Returns(Task.FromResult(true));

        var result = await _service.DeleteAsync(groupId);

        Assert.True(result);
        await _repository.Received(1).DeleteAsync(groupId);
    }

    [Fact]
    public async Task DeleteAsync_DoesNotRaiseStateChangedForBuiltInGroups()
    {
        var notifications = 0;
        _service.StateChanged += (_, _) => notifications++;

        await _service.DeleteAsync(KHostUserGroup.AdminGroupId);

        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task DeleteAsync_DeletesCustomGroups()
    {
        var groupId = Guid.NewGuid();
        _repository.DeleteAsync(groupId).Returns(Task.FromResult(true));

        var result = await _service.DeleteAsync(groupId);

        Assert.True(result);
        await _repository.Received(1).DeleteAsync(groupId);
    }
}
