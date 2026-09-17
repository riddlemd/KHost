namespace KHost.Abstractions.Models.Plugins;

/// <summary>What staging holds for the next start. Read fresh, so a crashed stage still shows.</summary>
public sealed record PluginStagingState
{
    public static readonly PluginStagingState Empty = new();

    /// <summary>Ids with a downloaded payload waiting to be moved into <c>plugins/</c>.</summary>
    public IReadOnlySet<Guid> Installs { get; init; } = new HashSet<Guid>();

    /// <summary>Folders marked for deletion, keyed by folder; two folders may share an id.</summary>
    public IReadOnlySet<string> Removals { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Ids whose staged payload could not be applied, and why. They stay staged, so the
    /// same failure would otherwise repeat silently on every start.</summary>
    public IReadOnlyDictionary<Guid, string> Failures { get; init; } = new Dictionary<Guid, string>();

    public bool IsEmpty => Installs.Count == 0 && Removals.Count == 0 && Failures.Count == 0;
}
