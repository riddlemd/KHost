using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

/// <summary>Users: singers and the host's own accounts, with their groups and provider ids.</summary>
/// <remarks>
/// <para>Host-implemented. A plugin goes through <see cref="Services.IUsersService"/>, which
/// announces <see cref="Messaging.Messages.UsersChanged"/>, refuses to delete the built-in users,
/// and saves a user's <see cref="KHostUser.Groups"/> and <see cref="KHostUser.ForeignKeys"/> as
/// memberships and keys. Creating or updating a user here with either collection filled does not
/// reconcile them that way and can overwrite the group rows themselves.</para>
/// <para>Names are unique without regard to case or accents: "Mike" and "mike" cannot both exist.
/// Search matches the name the same way; <see cref="UserSearchOptions.SingersOnly"/> leaves out
/// anyone in a group excluded from the singer queue. Search results and
/// <see cref="IRepository{T}.ReadAsync"/> come back with groups and provider ids loaded. Sort keys:
/// <c>name</c> (default), <c>createdDate</c>.</para>
/// </remarks>
public interface IUsersRepository : IRepository<KHostUser>
{
    /// <summary>The user with this name, or null; an exact spelling wins over one that differs only
    /// in case or accents.</summary>
    Task<KHostUser?> FindByNameAsync(string name);

    /// <summary>Whether any user is in an admin group, password or not.</summary>
    Task<bool> HasAdminUserAsync();

    /// <summary>An admin who can actually sign in; the guard against locking everyone out.</summary>
    Task<bool> HasAdminWithPasswordAsync();

    /// <summary>The singer a provider's id names, or null if unclaimed; the pair is unique.</summary>
    /// <remarks>Matched exactly, case included: an external id is not a name.</remarks>
    Task<KHostUser?> ReadByForeignKeyAsync(string source, string key);

    /// <summary>Claims <paramref name="key"/> for this singer; throws if another already holds it.</summary>
    /// <param name="userId">The singer the key is claimed for.</param>
    /// <param name="source">Names the provider the key belongs to.</param>
    /// <param name="key">The provider's own id for the singer; matched exactly, case included.</param>
    /// <param name="isEphemeral">True for a key the provider will drop in bulk with
    /// <see cref="DeleteEphemeralForeignKeysAsync"/>, for example when it reconnects.</param>
    /// <exception cref="Exceptions.KHostException">Another user already holds this source and key.</exception>
    Task AddForeignKeyAsync(Guid userId, string source, string key, bool isEphemeral);

    /// <summary>Gives up one key. Unknown pairs are not an error.</summary>
    Task RemoveForeignKeyAsync(Guid userId, string source, string key);

    /// <summary>Drops <paramref name="source"/>'s ephemeral keys; other keys stay untouched.</summary>
    /// <returns>How many keys were dropped.</returns>
    Task<int> DeleteEphemeralForeignKeysAsync(string source);
}
