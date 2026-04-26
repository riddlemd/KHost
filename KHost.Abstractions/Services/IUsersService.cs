using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IUsersService : IRepositoryService<KHostUser>
{
    Task ToggleIsRegularAsync(Guid userId);
    Task ToggleIsTipperAsync(Guid userId);
    Task<bool> HasAdminUserAsync();
}
