using KHost.UserInterface.Auth;
using KHost.UserInterface.Components;
using KHost.UserInterface.Http;

namespace KHost.UserInterface.Middleware;

/// <summary>The request pipeline, wired in the order each stage depends on the one before it.</summary>
internal static class PipelineExtensions
{
    internal static WebApplication UseKHostPipeline(this WebApplication app)
    {
        // Ahead of everything else, including static files: an off-box request must not reach the
        // UI, its assets, or its error pages.
        app.Use(async (context, next) =>
        {
            if (!LanAccessPolicy.IsAllowed(context.Connection.RemoteIpAddress, context.Request.Host, context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next();
        });

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
        }
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseAuthentication();

        // Login requirement off: every session is the console admin, and the gates stay wired and all pass
        // rather than a second code path. Read per request, so the toggle applies on the next page load.
        app.Use((context, next) =>
        {
            if (!(app.Configuration.GetValue<bool?>("Auth:RequireLogin") ?? true))
                context.User = KHostClaimsFactory.CreateConsolePrincipal();

            return next(context);
        });

        app.UseAuthorization();
        app.UseAntiforgery();

        app.UseStartupRedirect();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        return app;
    }
}
