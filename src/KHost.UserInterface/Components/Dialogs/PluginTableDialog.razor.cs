using KHost.Abstractions.Interactions.Requests;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models.Plugins;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

/// <summary>Draws a table a plugin described, since a plugin cannot ship markup of its own.</summary>
public partial class PluginTableDialog : IDisposable
{
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public ShowPluginTableRequest Request { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();
    private readonly CancellationTokenSource _closing = new();

    internal PluginTableContent _content = new();
    internal string? _error;
    internal bool _busy;

    internal IReadOnlyList<PluginTableRow> Rows => _content.Rows;

    private IReadOnlyList<PluginTableAction> TableActions => [.. _content.Actions];

    /// <summary>Read once per render: an empty actions column is a header over nothing.</summary>
    private bool ShowsActionsColumn => Rows.Any(row => row.Actions.Any());

    protected override async Task OnInitializedAsync()
    {
        _subscriptions.Add(Broker.Subscribe<PluginTableChanged>(_ => InvokeAsync(ReloadAsync)));

        await ReloadAsync();
    }

    internal async Task ReloadAsync()
    {
        try
        {
            _content = await Request.LoadAsync(_closing.Token);
        }
        catch (OperationCanceledException)
        {
            // The dialog closed mid-read; there is nothing left to draw it into.
            return;
        }
        catch (Exception)
        {
            // A plugin that cannot list its rows leaves the table as it was rather than emptying it.
            _error = "Could not read the list.";
        }

        StateHasChanged();
    }

    internal async Task RunAsync(PluginTableAction action)
    {
        _error = null;
        _busy = true;

        try { await action.PerformAsync(_closing.Token); }
        catch (OperationCanceledException) { return; }
        catch (Exception)
        {
            // The plugin's failure is its own; the dialog stays up so the host can try another row.
            _error = $"{action.DisplayName} did not work.";
        }
        finally
        {
            _busy = false;
        }

        await ReloadAsync();
    }

    private string Value(PluginTableRow row, PluginTableColumn column)
        => row.Fields.TryGetValue(column.Key, out var value) ? value : string.Empty;

    private string ColumnClass(PluginTableColumn column)
    {
        var kind = column.Kind == PluginTableColumnKind.Label
            ? "kh-plugin-table__col--label"
            : "kh-plugin-table__col--text";

        return column.Essential ? kind : $"{kind} kh-plugin-table__col--optional";
    }

    private static string StyleClass(PluginTableAction action)
        => action.Style is { Length: > 0 } style ? $"kh-button--{style}" : "kh-button--secondary";

    public record DialogRequest(ShowPluginTableRequest Table, Action? OnClose) : BaseDialogRequest(OnClose);

    public void Dispose()
    {
        _subscriptions.Dispose();
        _closing.Cancel();
        _closing.Dispose();
    }
}
