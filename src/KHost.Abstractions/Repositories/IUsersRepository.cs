using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

public interface IUsersRepository : IRepository<KHostUser>
{
    Task<KHostUser?> FindByNameAsync(string name);
    Task<bool> HasAdminUserAsync();

    /// <summary>An admin who can actually sign in — the guard against locking everyone out.</summary>
    Task<bool> HasAdminWithPasswordAsync();

    /// <summary>
    /// The singer a provider's own id names, or null if nobody claims it. The pair is unique, so
    /// this can only ever answer with one.
    /// </summary>
    Task<KHostUser?> ReadByForeignKeyAsync(string source, string key);

    /// <summary>
    /// Claims <paramref name="key"/> for this singer. Throws if another singer already holds it —
    /// re-pointing an external identity is a delete and an add, never a silent move.
    /// </summary>
    Task AddForeignKeyAsync(Guid userId, string source, string key, bool isEphemeral);

    /// <summary>Gives up one key. Unknown pairs are not an error.</summary>
    Task RemoveForeignKeyAsync(Guid userId, string source, string key);

    /// <summary>
    /// Drops <paramref name="source"/>'s ephemeral keys and returns how many went. Durable keys
    /// are never touched, and neither is any other source's — the host-wide sweep on the way up
    /// belongs to the database initializer, not to anything a provider can reach.
    /// </summary>
    Task<int> DeleteEphemeralForeignKeysAsync(string source);
}
