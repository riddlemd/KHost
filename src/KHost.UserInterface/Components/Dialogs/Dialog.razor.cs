using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KHost.UserInterface.Components.Dialogs;

public partial class Dialog
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private const string _rootClassName = "kh-dialog";
    private ElementReference _dialogRef;
    private bool _prevIsOpen;

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public bool CloseOnScrimClick { get; set; }
    [Parameter] public string Class { get; set; } = "";

    [Parameter] public RenderFragment? Header { get; set; }
    [Parameter] public RenderFragment? Body { get; set; }
    [Parameter] public RenderFragment? Footer { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

    public async Task CloseAsync()
    {
        IsOpen = false;

        await OnClose.InvokeAsync();
    }

    private async Task OnScrimClickAsync()
    {
        if (!CloseOnScrimClick) return;

        await CloseAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (IsOpen && !_prevIsOpen)
            await JS.InvokeVoidAsync("focusFirstInput", _dialogRef);

        _prevIsOpen = IsOpen;
    }
}
