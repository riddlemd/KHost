using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

public class UserGroupsService : BaseRepositoryService<KHostUserGroup, IUserGroupsRepository>, IUserGroupsService
{
    public UserGroupsService(ILogger<UserGroupsService> logger, IUserGroupsRepository repository)
        : base(logger, repository)
    {
    }

    public async Task AddUserToGroupAsync(Guid userId, Guid groupId)
    {
        await Repository.AddUserToGroupAsync(userId, groupId);
    }

    public async Task RemoveUserFromGroupAsync(Guid userId, Guid groupId)
    {
        await Repository.RemoveUserFromGroupAsync(userId, groupId);
    }

    public async Task<bool> IsUserInGroupAsync(Guid userId, Guid groupId)
    {
        return await Repository.IsUserInGroupAsync(userId, groupId);
    }

    public async Task<IReadOnlyList<KHostUser>> GetAllUsersInGroupAsync(Guid groupId)
    {
        return await Repository.GetAllUsersInGroupAsync(groupId);
    }
}
