using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>What the host is about to do with a song, so a gate can answer differently per moment.</summary>
/// <remarks>One question asked at three points rather than three methods that drift apart. A
/// provider whose answer never varies writes one check and ignores this.</remarks>
public enum MediaAction
{
    /// <summary>Putting it on the queue. Refusing here is the kindest refusal: a host finds out
    /// now, not with the singer already standing at the microphone.</summary>
    Queue,

    /// <summary>Making it playable, which for licensed content is the moment it leaves the
    /// provider's own container. A refusal here means no playable copy is ever written.</summary>
    Render,

    /// <summary>Putting it on the screens.</summary>
    Play,
}

/// <summary>Whether gated media may be used now, and why not when it may not.</summary>
public sealed record PlaybackGateResult(bool Allowed, string? Reason)
{
    /// <summary>Nothing stands in the way: the media is not gated, or the gate is satisfied.</summary>
    public static readonly PlaybackGateResult Ok = new(true, null);
}

/// <summary>A plugin's say over its own media, from the queue to the screen.</summary>
/// <remarks>Ownership and verdict are separate questions. Ownership is answered once, by the tag a
/// file carries or by <see cref="Claims"/> for a format that cannot carry one; the verdict is then
/// asked of that one gate rather than of every plugin.</remarks>
public interface IMediaPlaybackGate
{
    /// <summary>Metadata tag a gated file carries, holding the owning gate's key.</summary>
    public const string MetadataTag = "khost_provider";

    /// <summary>Assembly name, matched case-insensitively against <see cref="MetadataTag"/>.</summary>
    string ProviderId { get; }

    /// <summary>Whether this gate owns the file by its name, for content that cannot carry the tag.
    /// </summary>
    /// <remarks>The tag lives inside the container, so a format nothing can open has nowhere to put
    /// it: the provider's <c>.kit</c> is its own container, and the render that could carry a tag is a
    /// temporary file the library never points at. Answered from the path alone, and false by
    /// default, so a gate whose content is taggable need not think about it.</remarks>
    bool Claims(string filePath) => false;

    /// <summary>Whether this gate's media may be used for <paramref name="action"/>.</summary>
    /// <remarks>Asked on every load as well as every enqueue, so it stays cheap: the in-memory
    /// answer, not a round trip, unless the content is worth one.</remarks>
    Task<PlaybackGateResult> CanAsync(MediaAction action, Media media, CancellationToken cancellationToken = default);
}
