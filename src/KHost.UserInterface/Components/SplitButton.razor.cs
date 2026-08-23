using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace KHost.UserInterface.Components;

public partial class SplitButton : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    [Parameter] public EventCallback<MouseEventArgs> OnClick { get; set; }
    [Parameter] public RenderFragment? Text { get; set; }
    [Parameter] public RenderFragment? Buttons { get; set; }
    /// <summary>Disables the whole control, dropdown included.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>
    /// Disables only the primary action, leaving the dropdown reachable — for rows where the
    /// main action can't apply but the menu still offers something useful, like deleting.
    /// </summary>
    [Parameter] public bool PrimaryDisabled { get; set; }
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public string? Title { get; set; }

    private ElementReference _rootRef;
    private ElementReference _menuRef;
    private IJSObjectReference? _module;

    private bool _open;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "/js/dropdown-menu.js");

        if (_open && _module is not null)
            await _module.InvokeVoidAsync("positionMenu", _rootRef, _menuRef);
    }

    private void ToggleMenu() => _open = !_open;
    private void CloseMenu() => _open = false;

    public async ValueTask DisposeAsync()
    {
        try {
            if (_module is not null)
                await _module.DisposeAsync();
        }
        catch
        {
        }
    }
}
