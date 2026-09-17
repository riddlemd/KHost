using KHost.UserInterface.Services;

namespace KHost.UserInterface.Endpoints;

/// <summary>Serves themes a host authored at runtime.</summary>
/// <remarks>Built-ins compile to wwwroot as static files; these have none, so render on request.</remarks>
public static class ThemeStylesheetEndpoints
{
    public static IEndpointConventionBuilder MapThemeStylesheets(this IEndpointRouteBuilder endpoints)
        => endpoints.MapGet("/css/themes/custom/{themeId}.css", (
            string themeId,
            IThemeService themes,
            HttpContext context) =>
        {
            var theme = themes.Read(themeId);

            // Built-ins are excluded rather than redirected: one already has a static file at the
            // sibling route, and answering for it here would serve a second, derived copy of it.
            if (theme is null || theme.IsBuiltIn)
                return Results.NotFound();

            // The URL carries a hash of the theme's own values, so a given URL's bytes never change.
            context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";

            return Results.Text(ThemeCss.Build(theme), "text/css");
        })
        .AllowAnonymous();
}
