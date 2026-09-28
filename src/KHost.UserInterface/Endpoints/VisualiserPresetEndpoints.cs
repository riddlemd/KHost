using KHost.Abstractions.Services;
using KHost.Domain.Services.Visualisations;

namespace KHost.UserInterface.Endpoints;

/// <summary>An imported visualiser preset's file, for the local screen and the console's preview.</summary>
/// <remarks>Plain HTTP, no auth, like the levels beside it: a preset is the host's own decoration,
/// not a secret. The name is checked by the store before any path is built. Both callers fetch
/// from an opaque origin, so the fetch is cross-origin.</remarks>
public static class VisualiserPresetEndpoints
{
    public static IEndpointConventionBuilder MapVisualiserPresets(this IEndpointRouteBuilder endpoints)
        => endpoints.MapGet(VisualiserPresetService.RoutePrefix + "{name}", async (
            string name,
            IVisualiserPresetService presets,
            HttpContext context) =>
        {
            var json = await presets.ReadImportedAsync(name, context.RequestAborted);
            if (json is null) return Results.NotFound();

            context.Response.Headers.AccessControlAllowOrigin = "*";

            // The URL carries the file's write time, so a re-import is a new URL, never a stale hit.
            context.Response.Headers.CacheControl = "no-cache";

            return Results.Text(json, "application/json");
        })
        .AllowAnonymous();
}
