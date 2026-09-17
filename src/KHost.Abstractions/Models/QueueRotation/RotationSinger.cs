namespace KHost.Abstractions.Models.QueueRotation;

/// <summary>Snapshot of a queued singer, built per pass; strategies see only what rotation needs.</summary>
public sealed record RotationSinger
{
    public required Guid Id { get; init; }
    public DateTime? LastSangOn { get; init; }
    public IReadOnlyList<Guid> GroupIds { get; init; } = [];
}
