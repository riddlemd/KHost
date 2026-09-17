using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Whether gated media may play now, and why not when it may not.</summary>
public sealed record PlaybackGateResult(bool Allowed, string? Reason)
{
    /// <summary>Nothing stands in the way: the media is not gated, or the gate is satisfied.</summary>
    public static readonly PlaybackGateResult Ok = new(true, null);
}

/// <summary>A plugin gates its tagged media: <see cref="MetadataTag"/> = <see cref="ProviderId"/>.</summary>
public interface IMediaPlaybackGate
{
    /// <summary>Metadata tag a gated file carries, holding the owning gate's key.</summary>
    public const string MetadataTag = "khost_provider";

    /// <summary>Assembly name, matched case-insensitively against <see cref="MetadataTag"/>.</summary>
    string ProviderId { get; }

    /// <summary>Whether this gate's media may play; cheap on every load unless worth a round trip.</summary>
    Task<PlaybackGateResult> CanPlayAsync(Media media, CancellationToken cancellationToken = default);
}
