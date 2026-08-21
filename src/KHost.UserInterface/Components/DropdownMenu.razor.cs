using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KHost.UserInterface.Components;

public partial class DropdownMenu : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    [Parameter] public RenderFragment? Trigger { get; set; }
    [Parameter] public RenderFragment? Items { get; set; }
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public string? Title { get; set; }
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Set false for menus holding toggles, which should survive being clicked.</summary>
    [Parameter] public bool CloseOnItemClick { get; set; } = true;

    /// <summary>
    /// Raised when the menu opens or closes. Opening and closing are this component's own state, so
    /// without this a consumer never re-renders for them — and anything it draws inside the menu is
    /// left holding whatever it decided last time.
    /// </summary>
    [Parameter] public EventCallback<bool> OpenChanged { get; set; }

    private bool _open;

    private ElementReference _rootRef;
    private ElementReference _menuRef;
    private IJSObjectReference? _module;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "/js/dropdown-menu.js");

        if (_open && _module is not null)
            await _module.InvokeVoidAsync("positionMenu", _rootRef, _menuRef);
    }

    public void Close()
    {
        if (!_open) return;

        SetOpen(false);
        StateHasChanged();
    }

    private void Toggle() => SetOpen(!_open);

    private void OnMenuClick()
    {
        if (CloseOnItemClick)
            SetOpen(false);
    }

    private void SetOpen(bool open)
    {
        if (_open == open) return;

        _open = open;
        _ = OpenChanged.InvokeAsync(open);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is not null)
                await _module.DisposeAsync();
        }
        catch
        {
            // Circuit already gone; nothing to release.
        }
    }
}
