using KHost.Abstractions.Services;
using KHost.UserInterface.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace KHost.UserInterface.Endpoints;

/// <summary>Native form posts, not circuit calls: a cookie can only be issued on an HTTP response.</summary>
/// <remarks>Antiforgery is off: the console answers loopback only, so a forged post logs someone in or out.</remarks>
public static class AuthEndpoints
{
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/auth/login", async (
            HttpContext http,
            [FromForm] string username,
            [FromForm] string password,
            IAuthService authService,
            IUsersService usersService,
            ICacheService cacheService) =>
        {
            var result = await authService.LoginAsync(username, password);

            if (result is not { Success: true, User: { } user })
                return Results.Redirect("/login?failed=1");

            // Re-read for the groups: the login lookup returns the bare row, and the
            // principal's role and permission claims come from group membership.
            var withGroups = await usersService.ReadAsync(user.Id) ?? user;

            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                KHostClaimsFactory.Create(withGroups, CookieAuthenticationDefaults.AuthenticationScheme));

            // The canonical name, not the typed casing: the lock screen shows who was at the
            // controls, the way an OS lock screen would.
            await cacheService.SaveAsync(Program.LastLoginCacheKey, withGroups.Name);

            return Results.Redirect("/");
        }).AllowAnonymous().DisableAntiforgery();

        app.MapPost("/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        }).DisableAntiforgery();

        return app;
    }
}
