using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace KHost.UserInterface.Components;

/// <summary>
/// Type-to-search replacement for a native select, for lists too long to scroll. Binds the chosen
/// item itself, not a key, and takes every row from <see cref="Search"/> — so it works the same
/// against a list in memory, in SQL or over HTTP.
/// </summary>
/// <typeparam name="TItem">Whatever is being chosen.</typeparam>
public partial class ComboBox<TItem> : IAsyncDisposable
{
    /// <summary>Long enough that a typist is not searching on every keystroke, short enough to feel immediate.</summary>
    private const int DebounceMilliseconds = 150;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>
    /// Finds the rows for what has been typed. Cap the row count here — the box does not.
    /// </summary>
    [Parameter, EditorRequired] public Func<string, Task<IReadOnlyList<TItem>>>? Search { get; set; }

    /// <summary>What to show for a row, in the menu and in the field once it is chosen.</summary>
    [Parameter, EditorRequired] public Func<TItem, string> DisplayName { get; set; } = _ => "";

    /// <summary>Characters needed before the menu opens. Nothing opens it on focus.</summary>
    [Parameter] public int MinimumSearchLength { get; set; } = 3;

    [Parameter] public TItem? Value { get; set; }
    [Parameter] public EventCallback<TItem?> ValueChanged { get; set; }
    [Parameter] public string Placeholder { get; set; } = "Search…";
    [Parameter] public string Class { get; set; } = "";

    private ElementReference _inputRef;
    private IJSObjectReference? _module;
    private IJSObjectReference? _handle;

    private string _query = "";
    private List<TItem> _results = [];
    private bool _isOpen;
    private bool _isSearching;
    private int _highlighted;

    private TItem? _bound;
    private CancellationTokenSource? _debounce;

    protected override void OnParametersSet()
    {
        if (EqualityComparer<TItem>.Default.Equals(Value, _bound))
            return;

        _bound = Value;
        _query = Value is null ? "" : DisplayName(Value);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        _module = await JS.InvokeAsync<IJSObjectReference>("import", "/js/combobox-keys.js");
        _handle = await _module.InvokeAsync<IJSObjectReference>("init", _inputRef);
    }

    private bool CanSearch => _query.Trim().Length >= MinimumSearchLength;

    private void OnFocusOut() => Close();

    private async Task OnQueryChangedAsync(ChangeEventArgs e)
    {
        _query = e.Value?.ToString() ?? "";

        // The text and the bound value must never disagree, or a form saves the row that was
        // chosen before the name was edited.
        await SetValueAsync(default);

        _debounce?.Cancel();
        _debounce?.Dispose();
        // Cleared, not just disposed: the path below returns without replacing it, and cancelling
        // a disposed source throws out of DisposeAsync, which kills the circuit.
        _debounce = null;

        if (!CanSearch)
        {
            // Or the menu reopens later still showing answers to a query that has been deleted.
            _results = [];
            Close();
            return;
        }

        _debounce = new CancellationTokenSource();
        var token = _debounce.Token;

        _isSearching = true;
        _isOpen = true;

        try
        {
            await Task.Delay(DebounceMilliseconds, token);
            await SearchAsync(_query, token);
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke owns the search now.
        }
    }

    private async Task SearchAsync(string query, CancellationToken token = default)
    {
        if (Search is null) return;

        _isSearching = true;

        var results = await Search(query ?? "");

        if (token.IsCancellationRequested) return;

        _results = [.. results];
        _highlighted = 0;
        _isSearching = false;
        _isOpen = true;

        StateHasChanged();
    }

    private async Task OnKeyDownAsync(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "ArrowDown" when _isOpen && _results.Count > 0:
                _highlighted = (_highlighted + 1) % _results.Count;
                break;

            case "ArrowUp" when _isOpen && _results.Count > 0:
                _highlighted = (_highlighted - 1 + _results.Count) % _results.Count;
                break;

            case "Enter" when _isOpen && _highlighted < _results.Count:
                await SelectAsync(_results[_highlighted]);
                break;

            case "Escape":
                Close();
                break;

            case "ArrowDown" when !_isOpen && CanSearch:
                await SearchAsync(_query);
                break;
        }
    }

    private async Task SelectAsync(TItem item)
    {
        _query = DisplayName(item);
        await SetValueAsync(item);
        Close();
    }

    private async Task SetValueAsync(TItem? value)
    {
        if (EqualityComparer<TItem>.Default.Equals(Value, value)) return;

        // Ahead of the callback: the parent echoes the value straight back, and OnParametersSet
        // would read that as an outside change and rewrite the field.
        _bound = value;
        Value = value;

        await ValueChanged.InvokeAsync(value);
    }

    private void Close()
    {
        _isOpen = false;
        _isSearching = false;
    }

    public async ValueTask DisposeAsync()
    {
        _debounce?.Cancel();
        _debounce?.Dispose();

        try
        {
            if (_handle is not null)
            {
                await _handle.InvokeVoidAsync("dispose");
                await _handle.DisposeAsync();
            }
            if (_module is not null)
                await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit went away before the component did; there is nothing left to detach from.
        }

        GC.SuppressFinalize(this);
    }
}
