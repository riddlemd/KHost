using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Groups of users, and who belongs to which; what permissions and rotation rules such as a
/// VIP group are keyed on.</summary>
/// <remarks>A plugin may take it to read or change membership. The host's built-in groups cannot be
/// deleted: <see cref="IRepositoryService{T}.DeleteAsync"/> answers false for them. A host singleton,
/// callable from any thread. Group create, update and delete announce
/// <see cref="KHost.Abstractions.Messaging.Messages.UserGroupsChanged"/>; membership changes announce
/// nothing.</remarks>
public interface IUserGroupsService : IRepositoryService<KHostUserGroup>
{
    /// <summary>Puts a user in a group.</summary>
    Task AddUserToGroupAsync(Guid userId, Guid groupId);

    /// <summary>Takes a user out of a group.</summary>
    Task RemoveUserFromGroupAsync(Guid userId, Guid groupId);

    /// <summary>Whether the user belongs to the group.</summary>
    Task<bool> IsUserInGroupAsync(Guid userId, Guid groupId);

    /// <summary>Every member of the group.</summary>
    Task<IReadOnlyList<KHostUser>> GetAllUsersInGroupAsync(Guid groupId);
}
