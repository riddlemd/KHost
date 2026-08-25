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

    /// <summary>
    /// Which group a row belongs to. Rows are shown in the order <see cref="Search"/> returned them
    /// and a heading is drawn wherever the group changes, so the caller groups by sorting — the box
    /// never reorders. Return null or empty for rows that should carry no heading.
    /// </summary>
    [Parameter] public Func<TItem, string?>? GroupName { get; set; }

    /// <summary>Characters needed before the menu opens.</summary>
    [Parameter] public int MinimumSearchLength { get; set; } = 3;

    /// <summary>
    /// Opens the menu on focus while the field is empty, so a short list can be browsed the way a
    /// native select is. Off by default: against a list of every song in the library, a menu that
    /// opens itself is a wall of rows in front of the field a host meant to type into.
    /// </summary>
    [Parameter] public bool OpenWhenEmpty { get; set; }

    /// <summary>
    /// How many rows that opening shows. An empty field is not a search, so without a cap a box
    /// over a whole library answers it with the whole library.
    /// </summary>
    /// <remarks>
    /// Caps what is shown, not what <see cref="Search"/> fetches — the delegate is still where a
    /// caller keeps a query from reading its table end to end.
    /// </remarks>
    [Parameter] public int MaxRowsWhenEmpty { get; set; } = 10;

    [Parameter] public TItem? Value { get; set; }
    [Parameter] public EventCallback<TItem?> ValueChanged { get; set; }

    /// <summary>
    /// What is in the field. Bind it when the caller accepts text that matches no row — a new name
    /// being typed for the first time — or to clear the field from outside.
    /// </summary>
    [Parameter] public string? Text { get; set; }

    [Parameter] public EventCallback<string> TextChanged { get; set; }
    [Parameter] public string Placeholder { get; set; } = "Search…";
    [Parameter] public string Class { get; set; } = "";

    /// <summary>Names this input for the focus shortcut in shortcuts.js.</summary>
    [Parameter] public string? ShortcutId { get; set; }

    private ElementReference _inputRef;
    private IJSObjectReference? _module;
    private IJSObjectReference? _handle;

    private string _query = "";
    private List<TItem> _results = [];
    private bool _isOpen;
    private bool _isSearching;
    private bool _isTrimmed;
    private int _highlighted;

    private TItem? _bound;
    private string? _boundText;
    private CancellationTokenSource? _debounce;

    protected override void OnParametersSet()
    {
        // A changed row wins over changed text: picking one is what set the other.
        if (!EqualityComparer<TItem>.Default.Equals(Value, _bound))
        {
            _bound = Value;
            SetQuery(Value is null ? "" : DisplayName(Value));
            Dismiss();
        }
        else if (Text is not null && Text != _boundText)
        {
            SetQuery(Text);
            Dismiss();
        }
    }

    private void SetQuery(string text)
    {
        _query = text;
        _boundText = text;
    }

    private async Task SetTextAsync(string text)
    {
        SetQuery(text);

        await TextChanged.InvokeAsync(text);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        _module = await JS.InvokeAsync<IJSObjectReference>("import", "/js/combobox-keys.js");
        _handle = await _module.InvokeAsync<IJSObjectReference>("init", _inputRef);
    }

    private bool CanSearch => _query.Trim().Length >= MinimumSearchLength;

    private void OnFocusOut() => Close();

    /// <summary>
    /// Only while the field is empty: once a row is chosen its name is in the field, and reopening
    /// over it would put a menu between the host and what they already picked.
    /// </summary>
    private async Task OnFocusAsync()
    {
        if (!OpenWhenEmpty || _query.Trim().Length > 0)
            return;

        await SearchAsync("", limit: MaxRowsWhenEmpty);
    }

    private async Task OnQueryChangedAsync(ChangeEventArgs e)
    {
        await SetTextAsync(e.Value?.ToString() ?? "");

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

    private async Task SearchAsync(string query, CancellationToken token = default, int? limit = null)
    {
        if (Search is null) return;

        _isSearching = true;

        var results = await Search(query ?? "");

        if (token.IsCancellationRequested) return;

        _isTrimmed = limit is { } max && results.Count > max;
        _results = _isTrimmed ? [.. results.Take(limit!.Value)] : [.. results];
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

    /// <summary>A heading is drawn where a row's group differs from the row above it.</summary>
    private bool StartsGroup(int index)
    {
        if (GroupName is null) return false;

        var group = GroupName(_results[index]);
        if (string.IsNullOrEmpty(group)) return false;

        return index == 0 || GroupName(_results[index - 1]) != group;
    }

    private string? GroupOf(int index) => GroupName?.Invoke(_results[index]);

    private async Task SelectAsync(TItem item)
    {
        await SetTextAsync(DisplayName(item));
        await SetValueAsync(item);
        Close();
    }

    private async Task SetValueAsync(TItem? value)
    {
        if (EqualityComparer<TItem>.Default.Equals(Value, value)) return;

        // Ahead of the callback: the parent echoes the value straight back, and OnParametersSet
        // would read that as an outside change and rewrite the field.
        _bound = value;
        _boundText = _query;
        Value = value;

        await ValueChanged.InvokeAsync(value);
    }

    /// <summary>
    /// Closes and forgets the rows. Only for a change that came from the caller: what the menu was
    /// answering is gone with the text it was answering about.
    /// </summary>
    private void Dismiss()
    {
        _results = [];
        Close();
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
