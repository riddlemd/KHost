using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Asks the one gate that owns a file, so no caller has to know which that is.</summary>
/// <remarks>Ownership is <see cref="IMediaPlaybackGate.Claims"/> first, asked of every gate from the
/// path alone; only when no gate claims the name is the file's
/// <see cref="IMediaPlaybackGate.MetadataTag"/> read, and never for a file not yet on disk. Media
/// nobody owns always plays.
///
/// <para>A router, not a guard: the rule itself lives in the plugin. A plugin gates its content by
/// implementing <see cref="IMediaPlaybackGate"/>, not this. The host asks it on enqueue
/// (<see cref="MediaAction.Queue"/>) and on every load (<see cref="MediaAction.Play"/>). A host
/// singleton, callable from any thread. Announces nothing.</para></remarks>
public interface IMediaGateService
{
    /// <summary>The owning gate's verdict on <paramref name="action"/> for
    /// <paramref name="media"/>.</summary>
    /// <returns><see cref="PlaybackGateResult.Ok"/> when no gate owns the file, and also when the
    /// gate throws: a broken provider must not strand the night, so its failure is logged and the
    /// action allowed.</returns>
    /// <exception cref="OperationCanceledException">When <paramref name="cancellationToken"/> fires;
    /// the one failure passed on rather than allowed.</exception>
    Task<PlaybackGateResult> EvaluateAsync(MediaAction action, Media media, CancellationToken cancellationToken = default);
}
