namespace KHost.Abstractions.Models.Plugins;

/// <summary>
/// What the staging folder is holding for the next start. Read from disk rather than memory so a
/// stage made before a crash — or by a previous run — still shows on the page.
/// </summary>
public sealed record PluginStagingState
{
    public static readonly PluginStagingState Empty = new();

    /// <summary>Ids with a downloaded payload waiting to be moved into <c>plugins/</c>.</summary>
    public IReadOnlySet<Guid> Installs { get; init; } = new HashSet<Guid>();

    /// <summary>Folder names under <c>plugins/</c> marked for deletion on the next start. Named
    /// by folder rather than by id: two folders may carry one id, and a removal is a host pointing
    /// at one of them.</summary>
    public IReadOnlySet<string> Removals { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Ids whose staged payload could not be applied, and why. They stay staged, so the
    /// same failure would otherwise repeat silently on every start.</summary>
    public IReadOnlyDictionary<Guid, string> Failures { get; init; } = new Dictionary<Guid, string>();

    public bool IsEmpty => Installs.Count == 0 && Removals.Count == 0 && Failures.Count == 0;
}
