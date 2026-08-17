using KHost.Abstractions.Services;

namespace KHost.UserInterface.Endpoints;

/// <summary>
/// Serves the host's transcoded HLS to screens. Deliberately plain HTTP with no antiforgery or
/// auth: consumers include devices that cannot authenticate, and sessions are addressed by an
/// unguessable id that only lives as long as the song.
/// </summary>
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
                _ => "application/octet-stream",
            };

            // Cast receivers and cross-origin browser consumers both refuse the stream without this.
            context.Response.Headers.AccessControlAllowOrigin = "*";

            if (contentType.Contains("mpegurl", StringComparison.Ordinal))
            {
                // An EVENT playlist grows while the song transcodes, so it must never be cached.
                context.Response.Headers.CacheControl = "no-cache, no-store";

                // Transcoding outruns playback, so by the time a screen attaches the playlist
                // already holds a minute of segments and the player would join at the live edge —
                // starting the song part-way in. EXT-X-START pins every consumer to the top.
                var playlist = File.ReadAllText(path);
                if (!playlist.Contains("#EXT-X-START", StringComparison.Ordinal))
                    playlist = playlist.Replace(
                        "#EXTM3U",
                        "#EXTM3U\n#EXT-X-START:TIME-OFFSET=0,PRECISE=YES",
                        StringComparison.Ordinal);

                return Results.Text(playlist, contentType);
            }

            // Cast receivers fetch segments with ranged GETs.
            return Results.File(path, contentType, enableRangeProcessing: true);
        })
        .AllowAnonymous();
}
