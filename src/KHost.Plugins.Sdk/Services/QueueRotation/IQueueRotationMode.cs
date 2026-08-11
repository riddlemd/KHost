namespace KHost.Plugins.Sdk.Services.QueueRotation;

/// <summary>
/// A selectable rotation mode. Plugins implement this to add modes; the host lists them by
/// Name/Description and resolves the venue's configured mode by Id.
/// </summary>
public interface IQueueRotationMode : IQueueRotationStrategy
{
    /// <summary>Stable machine id (e.g. "fifo"). Ids already taken by the host or an earlier-loaded plugin win.</summary>
    string Id { get; }
    string Name { get; }
    string Description { get; }
}
