using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IUsersService : IRepositoryService<KHostUser>
{
    Task<bool> HasAdminUserAsync();

    Task<bool> HasAdminWithPasswordAsync();

    /// <summary>
    /// The singer a provider's own id names, or null if nobody claims it — how a returning guest
    /// is recognised without matching on a name they may have typed differently this time.
    /// </summary>
    Task<KHostUser?> ReadByForeignKeyAsync(string source, string key);

    /// <summary>
    /// Claims a provider's id for this singer. Prefer this over mutating
    /// <see cref="Models.KHostUser.ForeignKeys"/> and saving: an entity read without its keys is
    /// saved back without them, and this touches nothing else about the singer.
    /// </summary>
    Task AddForeignKeyAsync(Guid userId, string source, string key, bool isEphemeral = false);

    /// <summary>Gives up one key. Unknown pairs are not an error.</summary>
    Task RemoveForeignKeyAsync(Guid userId, string source, string key);

    /// <summary>
    /// Drops the ephemeral keys a source issued — what a provider calls when whatever those keys
    /// named has gone, such as a remote channel dropping every guest on a reconnect. Returns how
    /// many went; durable keys are never touched.
    /// </summary>
    Task<int> DeleteEphemeralForeignKeysAsync(string source);
}
