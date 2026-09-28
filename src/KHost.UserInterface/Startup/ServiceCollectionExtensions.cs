using KHost.Abstractions.Interactions;
using KHost.Abstractions.Interactions.Requests;
using KHost.Abstractions.Models;
using KHost.UserInterface.Auth;
using KHost.UserInterface.Interactions;
using KHost.UserInterface.Interactions.Handlers;
using KHost.UserInterface.Services;
using KHost.UserInterface.Services.RedirectProviders;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace KHost.UserInterface.Startup;

/// <summary>The registrations only this host needs — not <c>AddDomain</c>/<c>AddDataAccess</c>/
/// <c>AddPlugins</c>, which a plugin author would also expect to find in <c>ProjectExtensions</c>.</summary>
internal static class ServiceCollectionExtensions
{
    /// <summary>Cookie auth, one authorization policy per permission, and the cascading auth state
    /// the pages that gate on it need.</summary>
    internal static void AddKHostAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "khost.auth";
                options.LoginPath = "/login";
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                options.SlidingExpiration = true;
            });

        // One policy per permission, admins passing all of them, so a page can gate itself with
        // [Authorize(Policy = nameof(KHostPermission.X))] and no gate needs its own logic.
        var authorization = services.AddAuthorizationBuilder();
        foreach (var permission in Enum.GetValues<KHostPermission>())
        {
            authorization.AddPolicy(permission.ToString(), policy =>
                policy.RequireAssertion(context =>
                    context.User.IsInRole(KHostClaimsFactory.AdminRole)
                    || context.User.HasClaim(KHostClaimsFactory.PermissionClaim, permission.ToString())));
        }

        services.AddCascadingAuthenticationState();
    }

    /// <summary>The UI-only services: nothing here is on a plugin's own boundary.</summary>
    internal static void AddUserInterfaceServices(this IServiceCollection services)
    {
        services.AddScoped<IPermissionService, PermissionService>();
        // Scoped, not singleton: a control's pick belongs to the circuit that made it, and a
        // reconnecting browser is a new session rather than one resuming yesterday's choices.
        services.AddScoped<IControlState, ControlState>();
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IAppInfoService, AppInfoService>();
        services.AddSingleton<IExternalLinkService, ExternalLinkService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IStartupRedirectProvider, SetupRedirectProvider>();
        services.AddSingleton<IStartupRedirectProvider, CliStartupRedirectProvider>();
    }

    /// <summary>Bridges <see cref="IInteractionDispatcher"/> requests into dialogs.</summary>
    internal static void AddInteractionHandlers(this IServiceCollection services)
    {
        services.AddSingleton<IInteractionDispatcher, DialogInteractionDispatcher>();
        services.AddSingleton<IInteractionHandler<EditMediaRequest, Media?>, EditMediaDialogHandler>();
        services.AddSingleton<IInteractionHandler<ShowLyricsRequest>, ShowLyricsDialogHandler>();
        services.AddSingleton<IInteractionHandler<ShowPluginTableRequest>, ShowPluginTableDialogHandler>();
        services.AddSingleton<IInteractionHandler<ConfirmDuplicateSongRequest, bool>, ConfirmDuplicateSongHandler>();
        services.AddSingleton<IInteractionHandler<TextPromptRequest, IReadOnlyDictionary<string, string>?>, TextPromptDialogHandler>();
    }
}
