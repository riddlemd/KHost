using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Whether gated media may play now, and why not when it may not.</summary>
public sealed record PlaybackGateResult(bool Allowed, string? Reason)
{
    /// <summary>Nothing stands in the way — the media is not gated, or the gate is satisfied.</summary>
    public static readonly PlaybackGateResult Ok = new(true, null);
}

/// <summary>
/// A plugin that renders media it does not want played without a live entitlement implements this.
/// It stamps its own files with the tag <see cref="MetadataTag"/> set to its <see cref="GateKey"/>
/// (KaraFun renders a track from a paid account's .kit and does not want it played once that
/// account is signed out), and the host asks the matching gate before every load — see
/// <see cref="IMediaGateService"/>. A file with no such tag, or one whose key matches no loaded
/// gate, is never gated.
/// </summary>
public interface IMediaPlaybackGate
{
    /// <summary>
    /// The container metadata tag a gated file carries, holding the owning gate's key. The key is
    /// the plugin's own identifier — its assembly name, so it is unique without a registry — which
    /// is why the tag reads <c>khost_provider</c>.
    /// </summary>
    public const string MetadataTag = "khost_provider";

    /// <summary>
    /// The plugin's identifier (its assembly name, e.g. <c>KHost.Plugins.KaraFun</c>), matched
    /// case-insensitively against a file's <see cref="MetadataTag"/> value.
    /// </summary>
    string GateKey { get; }

    /// <summary>
    /// Whether a file this gate owns may play right now. Runs on every load of a marked file, so
    /// it stays cheap — the in-memory answer, not a round trip — unless a plugin decides the
    /// content is worth one.
    /// </summary>
    Task<PlaybackGateResult> CanPlayAsync(Media media, CancellationToken cancellationToken = default);
}
