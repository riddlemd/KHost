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
        _open = false;

        StateHasChanged();
    }

    private void Toggle() => _open = !_open;

    private void OnMenuClick()
    {
        if (CloseOnItemClick)
            _open = false;
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
