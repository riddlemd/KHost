using KHost.Abstractions.Models.Plugins;

namespace KHost.Abstractions.Interactions.Requests;

/// <summary>Asks the host to draw a table of the plugin's own rows in a dialog.</summary>
/// <remarks>A plugin cannot ship markup — its assembly is never given to the renderer — so this is
/// how it puts a list in front of a host: it names the columns, supplies the rows, and hands over
/// what each button does. The host owns the drawing and nothing else.
/// <para>Only the title and the columns are fixed. Everything else comes back from
/// <see cref="LoadAsync"/>, which the dialog re-reads after every action and whenever a
/// <c>PluginTableChanged</c> is announced, because the list moves while it is open.</para>
/// </remarks>
public sealed record ShowPluginTableRequest : IInteractionRequest
{
    public required string Title { get; init; }

    public required IReadOnlyList<PluginTableColumn> Columns { get; init; }

    public required Func<CancellationToken, Task<PluginTableContent>> LoadAsync { get; init; }

    /// <summary>One line under the title, for a standing fact about the whole table.</summary>
    public string? Note { get; init; }
}
