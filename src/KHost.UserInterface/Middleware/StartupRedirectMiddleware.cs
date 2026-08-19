using KHost.UserInterface.Http;
using KHost.UserInterface.Services;
using Microsoft.Extensions.Logging;

namespace KHost.UserInterface.Middleware;

public class StartupRedirectMiddleware(
    RequestDelegate next,
    IEnumerable<IStartupRedirectProvider> providers,
    ILogger<StartupRedirectMiddleware> logger)
{
    private static readonly string[] StaticContentPrefixes =
    [
        "/_framework",
        "/_blazor",
        "/css",
        "/scss",
        "/js",
        "/images",
        "/fonts",
        "/favicon",
        "/_content"
    ];

    private static readonly string[] StaticFileExtensions =
    [
        ".css",
        ".scss",
        ".js",
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".svg",
        ".webp",
        ".woff",
        ".woff2",
        ".ttf",
        ".eot",
        ".ico",
        ".map"
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";

        // The stream and the hub answer machines, not browsers, so they are exempt for the same
        // reason LanAccessPolicy lets them off-box — asking that list keeps the two from drifting.
        if (IsStaticContent(path) || LanAccessPolicy.IsMachineFacing(context.Request.Path))
        {
            await next(context);
            return;
        }

        try
        {
            foreach (var provider in providers)
            {
                if (await provider.ShouldRedirectAsync(context))
                {
                    context.Response.Redirect(await provider.GetRedirectPathAsync());
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Startup redirect provider threw an exception for path {Path}", path);
            throw;
        }

        await next(context);
    }

    private static bool IsStaticContent(string path)
    {
        if (StaticContentPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (StaticFileExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }
}

public static class StartupRedirectMiddlewareExtensions
{
    public static IApplicationBuilder UseStartupRedirect(this IApplicationBuilder app)
        => app.UseMiddleware<StartupRedirectMiddleware>();
}
