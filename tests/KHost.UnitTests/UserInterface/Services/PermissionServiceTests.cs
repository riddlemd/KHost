using System.Security.Claims;
using KHost.Abstractions.Models;
using KHost.UserInterface.Auth;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace KHost.UnitTests.UserInterface.Services;

public class PermissionServiceTests
{
    [Fact]
    public async Task HasAsync_IsTrue_ForAGrantedPermission()
    {
        var service = Service(new Claim(KHostClaimsFactory.PermissionClaim, nameof(KHostPermission.AddToQueue)));

        Assert.True(await service.HasAsync(KHostPermission.AddToQueue));
        Assert.False(await service.HasAsync(KHostPermission.DeleteMedia));
    }

    [Fact]
    public async Task HasAsync_IsTrue_ForEverything_WhenAdmin()
    {
        var service = Service(new Claim(ClaimTypes.Role, KHostClaimsFactory.AdminRole));

        Assert.True(await service.HasAsync(KHostPermission.DeleteMedia));
        Assert.True(await service.IsAdminAsync());
    }

    [Fact]
    public async Task HasAsync_IsFalse_ForTheAnonymousUser()
    {
        var service = Service();

        Assert.False(await service.HasAsync(KHostPermission.AddToQueue));
        Assert.False(await service.IsAdminAsync());
    }

    private static PermissionService Service(params Claim[] claims)
    {
        var identity = claims.Length > 0 ? new ClaimsIdentity(claims, "test") : new ClaimsIdentity();
        var provider = Substitute.For<AuthenticationStateProvider>();
        provider.GetAuthenticationStateAsync()
            .Returns(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity))));

        return new PermissionService(provider);
    }
}
