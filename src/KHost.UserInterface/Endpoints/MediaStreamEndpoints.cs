using KHost.Abstractions.Services;

namespace KHost.UserInterface.Endpoints;

/// <summary>Plain HTTP, no auth: consumers include devices that cannot authenticate.</summary>
/// <remarks>A session id is unguessable and lives only as long as the song.</remarks>
public static class MediaStreamEndpoints
{
    public static IEndpointConventionBuilder MapMediaStream(this IEndpointRouteBuilder endpoints)
        => endpoints.MapGet("/media/{sessionId}/{fileName}", (
            string sessionId,
            string fileName,
            IMediaStreamService streams,
            HttpContext context) =>
        {
            var path = streams.ResolveArtifact(sessionId, fileName);
            if (path is null) return Results.NotFound();

            var contentType = Path.GetExtension(path) switch
            {
                ".m3u8" => "application/vnd.apple.mpegurl",
                ".ts" => "video/mp2t",
                ".m4s" or ".mp4" => "video/mp4",

                // Named, not sniffed: served as octet-stream a browser estimates an Ogg's duration
                // from its nominal bitrate instead of reading the last page's granule, which on a
                // VBR stem reads back as minutes longer than the file is.
                ".ogg" or ".oga" => "audio/ogg",
                _ => "application/octet-stream",
            };

            // Cross-origin browser consumers and off-box player devices both refuse the stream without this.
            context.Response.Headers.AccessControlAllowOrigin = "*";

            if (contentType.Contains("mpegurl", StringComparison.Ordinal))
            {
                // An EVENT playlist grows while the song transcodes, so it must never be cached.
                context.Response.Headers.CacheControl = "no-cache, no-store";

                // Transcoding outruns playback, so a player would join at the live edge and start
                // the song part-way in. EXT-X-START pins every consumer to the top.
                var playlist = File.ReadAllText(path);
                if (!playlist.Contains("#EXT-X-START", StringComparison.Ordinal))
                    playlist = playlist.Replace(
                        "#EXTM3U",
                        "#EXTM3U\n#EXT-X-START:TIME-OFFSET=0,PRECISE=YES",
                        StringComparison.Ordinal);

                return Results.Text(playlist, contentType);
            }

            // Player devices off the box fetch segments with ranged GETs.
            return Results.File(path, contentType, enableRangeProcessing: true);
        })
        .AllowAnonymous();
}
