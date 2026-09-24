namespace KHost.Abstractions.Models.Plugins;

/// <summary>Everything about a plugin's table that moves while the dialog is open.</summary>
/// <remarks>One read rather than a delegate per part: a search that is running changes the rows,
/// the button that stops it and the line shown when nothing was found, and reading those
/// separately is how two of them end up disagreeing.</remarks>
public sealed record PluginTableContent
{
    /// <summary>The rows to draw. Empty shows <see cref="EmptyMessage"/> instead.</summary>
    public IReadOnlyList<PluginTableRow> Rows { get; init; } = [];

    /// <summary>Buttons for the table rather than a row — searching, refreshing, signing in.</summary>
    public IEnumerable<PluginTableAction> Actions { get; init; } = [];

    /// <summary>Shown in place of the table when there are no rows.</summary>
    public string EmptyMessage { get; init; } = "Nothing to show.";
}
