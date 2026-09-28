using KHost.Domain.Services.Displays.LocalScreen;

namespace KHost.UserInterface.Endpoints;

/// <summary>The playing song's levels for the local screen's visualiser.</summary>
/// <remarks>Plain HTTP, no auth, like the stream it sits beside: the token is unguessable and lives
/// only as long as the song. A request made before the read finishes waits for it, so the screen
/// fetches once rather than polling.</remarks>
public static class SongLevelsEndpoints
{
    public static IEndpointConventionBuilder MapSongLevels(this IEndpointRouteBuilder endpoints)
        => endpoints.MapGet(FfmpegSongLevelsService.RoutePrefix + "{token}", async (
            string token,
            ISongLevelsService levels,
            HttpContext context) =>
        {
            byte[]? track;

            try { track = await levels.ReadAsync(token, context.RequestAborted); }
            catch (OperationCanceledException) { return Results.Empty; }

            if (track is null) return Results.NotFound();

            // The screen's page has an opaque origin, so its fetch is cross-origin.
            context.Response.Headers.AccessControlAllowOrigin = "*";
            context.Response.Headers.CacheControl = "no-store";

            return Results.Bytes(track, "application/octet-stream");
        })
        .AllowAnonymous();
}
