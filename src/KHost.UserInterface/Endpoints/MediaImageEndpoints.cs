using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common.Media;

namespace KHost.UserInterface.Endpoints;

/// <summary>Serves stills to the screens.</summary>
/// <remarks>Same as the stream: a screen holds no credentials, nor reaches the host's filesystem.</remarks>
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

            // Refused by format, not type: this route is for pictures, not a song's file. Path
            // comes from the library, never the request, so there is nothing to traverse with.
            if (MediaFormats.ContentTypeFor(row.Format) is not { } contentType)
                return Results.NotFound();

            if (!File.Exists(row.FilePath))
                return Results.NotFound();

            context.Response.Headers.AccessControlAllowOrigin = "*";

            return Results.File(row.FilePath, contentType);
        })
        .AllowAnonymous();
}
