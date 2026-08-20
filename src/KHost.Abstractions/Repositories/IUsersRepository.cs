using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

public interface IUsersRepository : IRepository<KHostUser>
{
    Task<KHostUser?> FindByNameAsync(string name);
    Task<bool> HasAdminUserAsync();

    /// <summary>An admin who can actually sign in — the guard against locking everyone out.</summary>
    Task<bool> HasAdminWithPasswordAsync();
}
