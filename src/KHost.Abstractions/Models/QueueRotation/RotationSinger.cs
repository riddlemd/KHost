namespace KHost.Abstractions.Models.QueueRotation;

/// <summary>Snapshot of a queued singer, built per pass; strategies see only what rotation needs.</summary>
public sealed record RotationSinger
{
    /// <summary>The singer's own id.</summary>
    public required Guid Id { get; init; }

    /// <summary>When this singer last sang, in UTC. Null for a singer who has never sung.</summary>
    public DateTime? LastSangOn { get; init; }

    /// <summary>The groups this singer belongs to, e.g. for matching
    /// <see cref="QueueRotationConfig.VipGroupId"/>.</summary>
    public IReadOnlyList<Guid> GroupIds { get; init; } = [];
}
