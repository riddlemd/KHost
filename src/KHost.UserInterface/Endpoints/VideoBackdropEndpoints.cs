using KHost.Domain.Services.Displays.LocalScreen;

namespace KHost.UserInterface.Endpoints;

/// <summary>The video the local screen draws under a song's words.</summary>
/// <remarks>Plain HTTP, no auth, like the stream it sits beside: a token is unguessable and names
/// only a file the host chose to hand out.</remarks>
public static class VideoBackdropEndpoints
{
    public static IEndpointConventionBuilder MapVideoBackdrops(this IEndpointRouteBuilder endpoints)
        => endpoints.MapGet(VideoBackdropService.RoutePrefix + "{token}", (
            string token,
            IVideoBackdropService backdrops,
            HttpContext context) =>
        {
            if (backdrops.ResolveFile(token) is not { } path) return Results.NotFound();

            // The screen's page has an opaque origin, so its fetch is cross-origin.
            context.Response.Headers.AccessControlAllowOrigin = "*";

            // A video element seeks and loops with ranged GETs.
            return Results.File(path, "video/mp4", enableRangeProcessing: true);
        })
        .AllowAnonymous();
}
