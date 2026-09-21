namespace KHost.Abstractions.Models.Plugins;

/// <summary>One row of a plugin's table.</summary>
public sealed record PluginTableRow
{
    /// <summary>The plugin's own key for the row; the host uses it to tell rows apart while one
    /// of them is busy, and never interprets it.</summary>
    public required string Id { get; init; }

    /// <summary>Cell text keyed by <see cref="PluginTableColumn.Key"/>; a missing key is blank.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();

    public IEnumerable<PluginTableAction> Actions { get; init; } = [];

    /// <summary>Drawn as the row in effect — the device being used, the account signed in.</summary>
    public bool IsCurrent { get; init; }
}
