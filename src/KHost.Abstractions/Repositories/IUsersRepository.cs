using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

public interface IUsersRepository : IRepository<KHostUser>
{
    Task<KHostUser?> FindByNameAsync(string name);
    Task<bool> HasAdminUserAsync();

    /// <summary>An admin who can actually sign in; the guard against locking everyone out.</summary>
    Task<bool> HasAdminWithPasswordAsync();

    /// <summary>The singer a provider's id names, or null if unclaimed; the pair is unique.</summary>
    Task<KHostUser?> ReadByForeignKeyAsync(string source, string key);

    /// <summary>Claims <paramref name="key"/> for this singer; throws if another already holds it.</summary>
    Task AddForeignKeyAsync(Guid userId, string source, string key, bool isEphemeral);

    /// <summary>Gives up one key. Unknown pairs are not an error.</summary>
    Task RemoveForeignKeyAsync(Guid userId, string source, string key);

    /// <summary>Drops <paramref name="source"/>'s ephemeral keys; other keys stay untouched.</summary>
    Task<int> DeleteEphemeralForeignKeysAsync(string source);
}
