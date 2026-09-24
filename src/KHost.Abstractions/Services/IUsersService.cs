using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Everyone the host knows: singers, and the console's own accounts.</summary>
/// <remarks>A plugin may take it — typically to find or create the singer a remote guest is, and to
/// remember them by the provider's own id through a foreign key. Saving a user saves their group
/// memberships and foreign keys with them. The host's built-in users cannot be deleted:
/// <see cref="IRepositoryService{T}.DeleteAsync"/> answers false for them. Deleting a singer takes
/// them, and the songs they had waiting, out of the queue.
///
/// <para>A host singleton, callable from any thread. Every create, update, delete and foreign-key
/// change announces <see cref="KHost.Abstractions.Messaging.Messages.UsersChanged"/>.</para></remarks>
public interface IUsersService : IRepositoryService<KHostUser>
{
    /// <summary>Whether any user belongs to an admin group.</summary>
    Task<bool> HasAdminUserAsync();

    /// <summary>Whether any admin user has a password set, which is what signing in to the console
    /// needs.</summary>
    Task<bool> HasAdminWithPasswordAsync();

    /// <summary>The user with this name, or null; one user, not a paged, contains-matching search.
    /// </summary>
    /// <remarks>The exact spelling wins; failing that, a name equal once case and accents are
    /// folded away.</remarks>
    Task<KHostUser?> FindByNameAsync(string name);

    /// <summary>The singer a provider's id names, or null; recognises a returning guest by id.</summary>
    /// <remarks>Matched exactly, with case: a provider's id is not a name.</remarks>
    Task<KHostUser?> ReadByForeignKeyAsync(string source, string key);

    /// <summary>Claims a provider's id; better than mutating ForeignKeys and saving directly.</summary>
    /// <param name="userId">The singer the id is claimed for.</param>
    /// <param name="source">The provider's own source name.</param>
    /// <param name="key">The provider's id for the singer; matched exactly, case included.</param>
    /// <param name="isEphemeral">True for an id naming a connection rather than a person; every
    /// ephemeral key is dropped at the next start.</param>
    /// <exception cref="KHost.Abstractions.Exceptions.KHostException">When another user already holds
    /// that source and key; a pair names at most one user.</exception>
    Task AddForeignKeyAsync(Guid userId, string source, string key, bool isEphemeral = false);

    /// <summary>Gives up one key. Unknown pairs are not an error.</summary>
    Task RemoveForeignKeyAsync(Guid userId, string source, string key);

    /// <summary>Drops the ephemeral keys a source issued, e.g. on reconnect; durable keys stay.</summary>
    /// <returns>How many were dropped; announces only when that is more than zero.</returns>
    Task<int> DeleteEphemeralForeignKeysAsync(string source);
}
