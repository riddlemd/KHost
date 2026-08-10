using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace KHost.UnitTests.Domain.Services;

public class UserGroupsServiceTests
{
    private readonly ILogger<UserGroupsService> _logger = Substitute.For<ILogger<UserGroupsService>>();
    private readonly IUserGroupsRepository _repository = Substitute.For<IUserGroupsRepository>();
    private readonly UserGroupsService _service;

    public UserGroupsServiceTests()
    {
        _service = new UserGroupsService(_logger, _repository);
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
}
