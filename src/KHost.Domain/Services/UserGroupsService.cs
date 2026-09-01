using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using Microsoft.Extensions.Logging;
using KHost.Common.Repositories;

namespace KHost.Domain.Services;

public class UserGroupsService : BaseRepositoryService<KHostUserGroup, IUserGroupsRepository>, IUserGroupsService
{

    public UserGroupsService(ILogger<UserGroupsService> logger, IUserGroupsRepository repository, IMessageBroker broker)
        : base(logger, repository, broker, new UserGroupsChanged())
    {
    }

    public override async Task<bool> DeleteAsync(Guid id)
    {
        if (RepositoryModels.IsBuiltIn(id))
        {
            Logger.LogWarning("Refused to delete built-in user group {GroupId}", id);
            return false;
        }

        return await base.DeleteAsync(id);
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
