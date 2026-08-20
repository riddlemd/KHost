using System.Security.Claims;
using KHost.Abstractions.Models;
using KHost.UserInterface.Auth;

namespace KHost.UnitTests.UserInterface.Auth;

public class KHostClaimsFactoryTests
{
    [Fact]
    public void Create_CarriesIdentityAndName()
    {
        var user = User("Steve");

        var principal = KHostClaimsFactory.Create(user, "test");

        Assert.Equal("Steve", principal.Identity?.Name);
        Assert.Equal(user.Id.ToString(), principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.True(principal.Identity?.IsAuthenticated);
    }

    [Fact]
    public void Create_GrantsTheAdminRole_OnlyThroughAnAdminGroup()
    {
        var admin = User("Boss", new KHostUserGroup { Name = "Admins", IsAdmin = true });
        var regular = User("Steve", new KHostUserGroup { Name = "Singers", IsAdmin = false });

        Assert.True(KHostClaimsFactory.Create(admin, "test").IsInRole(KHostClaimsFactory.AdminRole));
        Assert.False(KHostClaimsFactory.Create(regular, "test").IsInRole(KHostClaimsFactory.AdminRole));
    }

    [Fact]
    public void Create_UnionsPermissionsAcrossGroups_WithoutDuplicates()
    {
        var user = User("Steve",
            new KHostUserGroup { Name = "A", Permissions = [KHostPermission.AddToQueue, KHostPermission.ReorderQueue] },
            new KHostUserGroup { Name = "B", Permissions = [KHostPermission.AddToQueue, KHostPermission.EditVenue] });

        var principal = KHostClaimsFactory.Create(user, "test");
        var granted = principal.FindAll(KHostClaimsFactory.PermissionClaim).Select(c => c.Value).ToList();

        Assert.Equal(3, granted.Count);
        Assert.Contains(nameof(KHostPermission.AddToQueue), granted);
        Assert.Contains(nameof(KHostPermission.ReorderQueue), granted);
        Assert.Contains(nameof(KHostPermission.EditVenue), granted);
    }

    private static KHostUser User(string name, params KHostUserGroup[] groups)
        => new() { Name = name, Groups = [.. groups] };
}
