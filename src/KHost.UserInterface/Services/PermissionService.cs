using KHost.Abstractions.Models;
using KHost.UserInterface.Auth;
using Microsoft.AspNetCore.Components.Authorization;

namespace KHost.UserInterface.Services;

// Scoped, not singleton: the answer belongs to one circuit's signed-in user.
internal sealed class PermissionService : IPermissionService
{
    private readonly AuthenticationStateProvider _authenticationState;

    public PermissionService(AuthenticationStateProvider authenticationState)
        => _authenticationState = authenticationState;

    public async Task<bool> IsAdminAsync()
        => (await _authenticationState.GetAuthenticationStateAsync()).User.IsInRole(KHostClaimsFactory.AdminRole);

    public async Task<bool> HasAsync(KHostPermission permission)
    {
        var user = (await _authenticationState.GetAuthenticationStateAsync()).User;

        return user.IsInRole(KHostClaimsFactory.AdminRole)
            || user.HasClaim(KHostClaimsFactory.PermissionClaim, permission.ToString());
    }
}
