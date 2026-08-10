using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

public interface IUserGroupsRepository : IRepository<KHostUserGroup>
{
    Task AddUserToGroupAsync(Guid userId, Guid groupId);
    Task RemoveUserFromGroupAsync(Guid userId, Guid groupId);
    Task<bool> IsUserInGroupAsync(Guid userId, Guid groupId);
    Task<IReadOnlyList<KHostUser>> GetAllUsersInGroupAsync(Guid groupId);
}
