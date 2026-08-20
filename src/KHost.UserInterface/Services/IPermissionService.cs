using KHost.Abstractions.Models;

namespace KHost.UserInterface.Services;

/// <summary>What the signed-in user may do, for components deciding which controls to render.</summary>
public interface IPermissionService
{
    Task<bool> HasAsync(KHostPermission permission);

    Task<bool> IsAdminAsync();
}
