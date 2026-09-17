namespace KHost.Abstractions.Services.QueueRotation;

/// <summary>A selectable rotation mode; plugins add modes, resolved by the venue's configured Id.</summary>
public interface IQueueRotationMode : IQueueRotationStrategy
{
    /// <summary>Stable machine id (e.g. "fifo"); an id already taken by the host or a plugin wins.</summary>
    string Id { get; }
    string Name { get; }
    string Description { get; }
}
