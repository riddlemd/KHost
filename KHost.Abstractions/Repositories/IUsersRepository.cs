using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

public interface IUsersRepository : IRepository<KHostUser>
{
    Task<KHostUser?> FindByNameAsync(string name);
    Task<bool> HasAdminUserAsync();
}
