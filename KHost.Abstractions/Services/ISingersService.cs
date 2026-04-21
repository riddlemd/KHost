using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface ISingersService : IRepositoryService<Singer>
{
    Task ToggleIsRegularAsync(Guid singerId);
    Task ToggleIsTipperAsync(Guid singerId);
}
