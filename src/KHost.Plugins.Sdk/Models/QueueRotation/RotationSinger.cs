namespace KHost.Plugins.Sdk.Models.QueueRotation;

/// <summary>
/// Snapshot of a queued singer, built by the host per rotation pass. Strategies see only
/// what rotation needs — not the host's full user model.
/// </summary>
public sealed record RotationSinger
{
    public required Guid Id { get; init; }
    public DateTime? LastSangOn { get; init; }
    public DateTime? LastTippedOn { get; init; }
    public IReadOnlyList<Guid> GroupIds { get; init; } = [];
}
