using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IUsersService : IRepositoryService<KHostUser>
{
    Task<bool> HasAdminUserAsync();

    Task<bool> HasAdminWithPasswordAsync();

    /// <summary>The singer with exactly this name, or null; not a paged, contains-matching search.</summary>
    Task<KHostUser?> FindByNameAsync(string name);

    /// <summary>The singer a provider's id names, or null; recognises a returning guest by id.</summary>
    Task<KHostUser?> ReadByForeignKeyAsync(string source, string key);

    /// <summary>Claims a provider's id; better than mutating ForeignKeys and saving directly.</summary>
    Task AddForeignKeyAsync(Guid userId, string source, string key, bool isEphemeral = false);

    /// <summary>Gives up one key. Unknown pairs are not an error.</summary>
    Task RemoveForeignKeyAsync(Guid userId, string source, string key);

    /// <summary>Drops the ephemeral keys a source issued, e.g. on reconnect; durable keys stay.</summary>
    Task<int> DeleteEphemeralForeignKeysAsync(string source);
}
