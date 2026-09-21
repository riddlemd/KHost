using KHost.Abstractions.Services;
using KHost.Common.Media;

namespace KHost.UserInterface.Endpoints;

/// <summary>Serves the stills a venue picks its song backgrounds by.</summary>
/// <remarks>The folders are machine settings, so the browser cannot reach the files directly and
/// they cannot be served from wwwroot.
/// <para>The request names a background, never a path: the file served is one the folder read
/// actually returned, so there is nothing here to traverse with and no folder to point somewhere
/// it should not go. A name nothing answers to is simply not found.</para></remarks>
public static class BackgroundStillEndpoints
{
    public static IEndpointConventionBuilder MapBackgroundStills(this IEndpointRouteBuilder endpoints)
        => endpoints.MapGet("/venue/background-still", async (
            string file,
            IBackgroundPackService packs,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var pack = await packs.ReadAsync(cancellationToken);

            if (pack.Entries.FirstOrDefault(entry =>
                    string.Equals(entry.File, file, StringComparison.OrdinalIgnoreCase)) is not { } match)
                return Results.NotFound();

            if (match.StillPath is not { } still || !File.Exists(still))
                return Results.NotFound();

            if (MediaFormats.ContentTypeFor(Path.GetExtension(still).TrimStart('.')) is not { } contentType)
                return Results.NotFound();

            // Re-read whenever the folder changes, rather than pinning a still for the session.
            context.Response.Headers.CacheControl = "no-cache";

            return Results.File(still, contentType);
        });
}
