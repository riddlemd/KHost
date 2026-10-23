using KHost.UserInterface.Components;
using KHost.UserInterface.Http;

namespace KHost.UserInterface.Middleware;

/// <summary>The request pipeline, wired in the order each stage depends on the one before it.</summary>
internal static class PipelineExtensions
{
    /// <param name="app">The host.</param>
    /// <param name="serveConsole">Whether the console is served to browser tabs; the window renders it itself.</param>
    internal static WebApplication UseKHostPipeline(this WebApplication app, bool serveConsole)
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

        // The window asks the host only for endpoints: media, plugin icons and custom themes.
        if (!serveConsole) return app;

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
        }
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseAntiforgery();

        app.UseStartupRedirect();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        return app;
    }
}
