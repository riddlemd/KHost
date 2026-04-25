using KHost.UserInterface.Services;

namespace KHost.UserInterface.Middleware;

public class StartupRedirectMiddleware(
    RequestDelegate next,
    IEnumerable<IStartupRedirectProvider> providers)
{
    public async Task InvokeAsync(HttpContext context)
    {
        foreach (var provider in providers)
        {
            if (await provider.ShouldRedirectAsync(context))
            {
                context.Response.Redirect(await provider.GetRedirectPathAsync());
                return;
            }
        }

        await next(context);
    }
}

public static class StartupRedirectMiddlewareExtensions
{
    public static IApplicationBuilder UseStartupRedirect(this IApplicationBuilder app)
        => app.UseMiddleware<StartupRedirectMiddleware>();
}
