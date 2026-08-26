using KHost.Abstractions.Models.Plugins;

namespace KHost.CatalogSync;

/// <summary>What one synced release says about a plugin, gathered from the payload and the repo.</summary>
public sealed record SyncFacts
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string? Author { get; init; }

    public string? Description { get; init; }

    public required string Repository { get; init; }

    public IReadOnlyList<string> Capabilities { get; init; } = [];

    public required PluginCatalogRelease Release { get; init; }
}

public static class CatalogMerge
{
    /// <summary>
    /// Folds one release into the catalog in place. Re-running with the same release is a no-op, so
    /// the tool can be pointed at a tag twice without producing a diff.
    /// </summary>
    public static void Apply(PluginCatalog catalog, SyncFacts facts)
    {
        var entry = catalog.Plugins.Find(p => p.Id == facts.Id);

        if (entry is null)
        {
            entry = new PluginCatalogEntry { Id = facts.Id };
            catalog.Plugins.Add(entry);
        }

        // Only blanks are filled from the payload. A description someone wrote for the browse list
        // is usually better than the manifest's, and a sync must not undo that edit.
        if (string.IsNullOrWhiteSpace(entry.Name)) entry.Name = facts.Name;
        if (string.IsNullOrWhiteSpace(entry.Author)) entry.Author = facts.Author;
        if (string.IsNullOrWhiteSpace(entry.Description)) entry.Description = facts.Description;
        if (string.IsNullOrWhiteSpace(entry.Repository)) entry.Repository = facts.Repository;
        if (entry.Capabilities.Count == 0 && facts.Capabilities.Count > 0)
            entry.Capabilities = [.. facts.Capabilities];

        // Re-syncing a tag replaces its release rather than listing it twice.
        entry.Releases.RemoveAll(r => string.Equals(r.Version, facts.Release.Version, StringComparison.OrdinalIgnoreCase));
        entry.Releases.Add(facts.Release);

        entry.Releases.Sort((a, b) =>
        {
            var byVersion = PluginVersion.Parse(b.Version).CompareTo(PluginVersion.Parse(a.Version));

            // Ordinal tiebreak so two releases parsing equal (1.0 and 1.0.0) still order the same
            // way on every run, which is what keeps a re-sync diff-free.
            return byVersion != 0 ? byVersion : string.CompareOrdinal(b.Version, a.Version);
        });

        catalog.Plugins.Sort((a, b) =>
        {
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

            return byName != 0 ? byName : a.Id.CompareTo(b.Id);
        });
    }
}
