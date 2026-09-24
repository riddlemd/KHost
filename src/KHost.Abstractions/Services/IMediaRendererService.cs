using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Asks whichever renderer owns this file, so no caller has to know which.</summary>
public interface IMediaRendererService
{
    /// <summary>What to play for this file on this target. Never null — something always plays.</summary>
    /// <remarks>The fallback encodes anything nothing else claimed, which is what makes this total.</remarks>
    Task<MediaRendition> RenderAsync(
        MediaRenderRequest request,
        CancellationToken cancellationToken = default);
}
