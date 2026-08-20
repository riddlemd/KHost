using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IUsersService : IRepositoryService<KHostUser>
{
    Task<bool> HasAdminUserAsync();

    Task<bool> HasAdminWithPasswordAsync();
}
