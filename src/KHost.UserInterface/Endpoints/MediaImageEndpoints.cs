using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

namespace KHost.UserInterface.Endpoints;

/// <summary>
/// Serves stills to the screens. Plain HTTP for the same reason the stream is: a screen holds no
/// credentials, and it can reach nothing on the host's filesystem itself.
/// </summary>
public static class MediaImageEndpoints
{
    public static IEndpointConventionBuilder MapMediaImages(this IEndpointRouteBuilder endpoints)
        => endpoints.MapGet("/media/image/{mediaId:guid}", async (
            Guid mediaId,
            IMediaService media,
            HttpContext context) =>
        {
            var row = await media.ReadAsync(mediaId);

            if (row is null)
                return Results.NotFound();

            // Refused by format rather than by kind: this route exists to put a picture on a
            // screen, and handing out a song's file through it is not that. The path itself comes
            // from the library, never from the request, so there is nothing to traverse with.
            if (MediaFormats.ContentTypeFor(row.Format) is not { } contentType)
                return Results.NotFound();

            if (!File.Exists(row.FilePath))
                return Results.NotFound();

            context.Response.Headers.AccessControlAllowOrigin = "*";

            return Results.File(row.FilePath, contentType);
        })
        .AllowAnonymous();
}
