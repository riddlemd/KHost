using System.Security.Claims;
using KHost.Abstractions.Models;

namespace KHost.UserInterface.Auth;

/// <summary>
/// Translates a KHost user into the principal the cookie carries. Admins get a role rather than
/// every permission claim, so a group edit never has to reissue their cookie.
/// </summary>
internal static class KHostClaimsFactory
{
    internal const string AdminRole = "Admin";
    internal const string PermissionClaim = "khost:permission";

    /// <summary>
    /// The identity every session gets while the login requirement is off: the pipeline and
    /// every permission gate stay exactly as built, and all of them pass.
    /// </summary>
    internal static ClaimsPrincipal CreateConsolePrincipal()
        => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "Console"),
            new Claim(ClaimTypes.Role, AdminRole),
        ], "console"));

    internal static ClaimsPrincipal Create(KHostUser user, string authenticationScheme)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
        };

        if (user.Groups.Any(g => g.IsAdmin))
            claims.Add(new Claim(ClaimTypes.Role, AdminRole));

        foreach (var permission in user.Groups.SelectMany(g => g.Permissions).Distinct())
            claims.Add(new Claim(PermissionClaim, permission.ToString()));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationScheme));
    }
}
