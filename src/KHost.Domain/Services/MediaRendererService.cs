using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

/// <inheritdoc />
public sealed class MediaRendererService(
    ILogger<MediaRendererService> logger,
    IEnumerable<IMediaRenderer> renderers,
    [FromKeyedServices(MediaRendererService.FallbackKey)] IMediaRenderer fallback,
    IStemMixdown mixdown) : IMediaRendererService
{
    /// <summary>Registration key for the renderer of last resort.</summary>
    /// <remarks>Keyed so it stays out of <c>IEnumerable&lt;IMediaRenderer&gt;</c> altogether: it
    /// claims every file, so reached as one of the plugin renderers it would answer for whatever
    /// happened to be registered after it, and the loader decides when a plugin's registrations
    /// land. The same reasoning as <see cref="MediaProbeService.FallbackKey"/>.</remarks>
    public const string FallbackKey = "khost.renderer.fallback";

    // Belt and braces against the same instance also arriving through the open registration.
    private readonly IReadOnlyList<IMediaRenderer> _renderers =
        renderers.Where(renderer => renderer != fallback).ToList();

    public async Task<MediaRendition> RenderAsync(
        MediaRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        foreach (var renderer in _renderers)
        {
            bool claimed;

            try
            {
                claimed = renderer.CanRender(request.FilePath);
            }
            catch (Exception ex)
            {
                // A renderer that throws deciding whether a file is its own must not stop the song:
                // the fallback can still encode it.
                logger.LogWarning(ex, "A renderer failed deciding whether it owns '{FilePath}'", request.FilePath);
                continue;
            }

            if (!claimed) continue;

            MediaRendition? rendition;

            try
            {
                rendition = await renderer.RenderAsync(request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (KHostException)
            {
                // Already says what a host can do about it; wrapping would bury that behind a
                // generic "couldn't prepare that song".
                throw;
            }
            catch (Exception ex)
            {
                // Its own format, and it could not produce anything. Handing the original to the
                // fallback would only reach ffmpeg with a container it cannot open either, so the
                // song fails here with a cause rather than as "Invalid data found".
                throw new KHostException(
                    "KHost couldn't prepare that song for the screens.",
                    "Check the file is still on the drive, then try again.",
                    "KH-RENDER",
                    ex);
            }

            // Stems carry no key, speed or picture, so what this target needs from them is decided
            // here rather than by each renderer that supplies them.
            if (rendition is not null)
                return StemMixdown.IsNeeded(rendition, request)
                    ? await mixdown.EncodeAsync(rendition, request, cancellationToken)
                    : rendition;

            // Null is "nothing better for this target", not a failure.

            break;
        }

        return await fallback.RenderAsync(request, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The fallback renderer produced nothing for '{request.FilePath}'.");
    }
}
