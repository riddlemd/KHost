using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Asks whichever renderer owns this file, so no caller has to know which.</summary>
/// <remarks>The router over every <see cref="IMediaRenderer"/>, and what playback calls when a song
/// starts. A plugin supplies a renderer by implementing <see cref="IMediaRenderer"/>, not this. A
/// host singleton, callable from any thread. Announces nothing.</remarks>
public interface IMediaRendererService
{
    /// <summary>What to play for this file on this target. Never null — something always plays.</summary>
    /// <remarks>The host's own encode takes anything nothing else claimed, and anything its claiming
    /// renderer declined, which is what makes this total. Stems the target cannot take as they are come
    /// back encoded into one stream. The caller owns the returned
    /// <see cref="MediaRendition.Session"/> and closes it.</remarks>
    /// <exception cref="KHost.Abstractions.Exceptions.KHostException">When a claiming renderer cannot
    /// produce the song; carries a line for the host.</exception>
    /// <exception cref="FileNotFoundException">When the host's own encode is asked for a file that is
    /// not on disk. That path may also fail with other exceptions, such as an encode that never
    /// starts; treat any of them as a song that cannot be played.</exception>
    /// <exception cref="OperationCanceledException">When <paramref name="cancellationToken"/>
    /// fires.</exception>
    Task<MediaRendition> RenderAsync(
        MediaRenderRequest request,
        CancellationToken cancellationToken = default);
}
