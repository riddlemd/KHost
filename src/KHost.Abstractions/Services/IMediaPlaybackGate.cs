using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>What the host is about to do with a song, so a gate can answer differently per moment.</summary>
/// <remarks>One question asked at several points rather than several methods that drift apart. A
/// provider whose answer never varies writes one check and ignores this.</remarks>
public enum MediaAction
{
    /// <summary>Putting it on the queue. Refusing here is the kindest refusal: a host finds out
    /// now, not with the singer already standing at the microphone.</summary>
    Queue,

    /// <summary>Making it playable, which for licensed content is the moment it leaves the
    /// provider's own container.</summary>
    /// <remarks>Not asked by the host at present. It stays so a gate can answer it once the host
    /// asks again; answer it as you would <see cref="Play"/>.</remarks>
    Render,

    /// <summary>Loading it to play, asked on every load.</summary>
    Play,
}

/// <summary>Whether gated media may be used now, and why not when it may not.</summary>
public sealed record PlaybackGateResult(bool Allowed, string? Reason)
{
    /// <summary>Nothing stands in the way: the media is not gated, or the gate is satisfied.</summary>
    public static readonly PlaybackGateResult Ok = new(true, null);
}

/// <summary>A plugin's say over its own media, from the queue to the screen.</summary>
/// <remarks>An extension point: a plugin IMPLEMENTS it so a subscription provider can honour its own
/// terms. The host routes each question to the one gate that owns the file and does what it
/// answers; it adds no enforcement of its own, and the gate is not meant to be airtight against a
/// host who owns the machine.
///
/// <para>Ownership and verdict are separate questions. Ownership is asked of every gate's
/// <see cref="Claims"/> first, from the path alone; only when none claims the file is the tag the
/// file carries read. The verdict is then asked of that one gate rather than of every plugin.</para>
///
/// <para>The plugin's object is one singleton shared across every extension interface it
/// implements, and is called from any thread.</para></remarks>
public interface IMediaPlaybackGate
{
    /// <summary>Metadata tag a gated file carries, holding the owning gate's key.</summary>
    public const string MetadataTag = "khost_provider";

    /// <summary>The key this gate answers to: the value its files carry in
    /// <see cref="MetadataTag"/>, matched case-insensitively. Conventionally the plugin's assembly
    /// name.</summary>
    /// <remarks>Must be unique among loaded gates; when two share one, only the first is
    /// asked.</remarks>
    string ProviderId { get; }

    /// <summary>Whether this gate owns the file by its name, for content that cannot carry the tag.
    /// </summary>
    /// <remarks>The tag lives inside the container, so a format nothing else can open has nowhere to
    /// put it. Answered from the path alone: it is asked for every queued turn on every reconcile, and
    /// opening the file would turn a bulk enqueue into thousands of reads.
    ///
    /// <para>Has a default body returning false, so a gate whose files carry the tag need not think
    /// about it. A gate whose files cannot carry one must override it, or its content is never
    /// gated.</para></remarks>
    bool Claims(string filePath) => false;

    /// <summary>Whether this gate's media may be used for <paramref name="action"/>.</summary>
    /// <returns><see cref="PlaybackGateResult.Ok"/> to allow. A refusal's
    /// <see cref="PlaybackGateResult.Reason"/> is flashed to the host, so write a line a host can act
    /// on; a refusal with no reason flashes a generic one at enqueue and nothing at load.</returns>
    /// <remarks>Asked on every load as well as every enqueue, so it stays cheap: the in-memory
    /// answer, not a round trip, unless the content is worth one. May raise a sign-in first. A gate
    /// that throws is logged and the action goes ahead.</remarks>
    Task<PlaybackGateResult> CanAsync(MediaAction action, Media media, CancellationToken cancellationToken = default);
}
