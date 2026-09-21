namespace KHost.Abstractions.Models.Plugins;

/// <summary>How the console lays a column out. The plugin hands raw text; the host draws it.</summary>
public enum PluginTableColumnKind
{
    /// <summary>An equal share of the table's width.</summary>
    Text,

    /// <summary>Short text sized to its own words, rather than an equal share.</summary>
    Label,
}

/// <summary>One column of a plugin's table, left to right.</summary>
/// <remarks>The plugin names a column and fills it by key; it never says how wide, what colour or
/// what font, for the same reason a manifest names an icon instead of supplying one.</remarks>
public sealed record PluginTableColumn
{
    /// <summary>Looks the cell up in <see cref="PluginTableRow.Fields"/>.</summary>
    public required string Key { get; init; }

    public required string Header { get; init; }

    public PluginTableColumnKind Kind { get; init; } = PluginTableColumnKind.Text;

    /// <summary>False drops this column when narrow; the first column is never dropped.</summary>
    public bool Essential { get; init; } = true;
}
