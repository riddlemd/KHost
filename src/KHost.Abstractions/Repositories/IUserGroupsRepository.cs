using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

/// <summary>User groups and their memberships.</summary>
/// <remarks>
/// <para>Host-implemented. A plugin goes through <see cref="Services.IUserGroupsService"/>, which
/// announces <see cref="Messaging.Messages.UserGroupsChanged"/> and refuses to delete the built-in
/// groups; deleting here does not.</para>
/// <para>Search matches the group name, case- and accent-insensitive. Sort keys: <c>name</c>
/// (default), <c>isAdmin</c>.</para>
/// </remarks>
public interface IUserGroupsRepository : IRepository<KHostUserGroup>
{
    /// <summary>Adds the membership; a no-op when the user is already in the group.</summary>
    Task AddUserToGroupAsync(Guid userId, Guid groupId);

    /// <summary>Removes the membership.</summary>
    /// <remarks>Unlike <see cref="AddUserToGroupAsync"/> this is not idempotent: removing a
    /// membership that does not exist throws. Check <see cref="IsUserInGroupAsync"/> first when
    /// unsure.</remarks>
    Task RemoveUserFromGroupAsync(Guid userId, Guid groupId);

    /// <summary>Whether the user is a member of the group.</summary>
    Task<bool> IsUserInGroupAsync(Guid userId, Guid groupId);

    /// <summary>Every member of the group, each with their groups loaded; empty when there are none.</summary>
    Task<IReadOnlyList<KHostUser>> GetAllUsersInGroupAsync(Guid groupId);
}
