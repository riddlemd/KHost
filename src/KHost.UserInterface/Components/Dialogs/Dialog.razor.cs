using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
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

    /// <summary>Esc closes it as the header X does. Off by default: a dialog holding a ComboBox
    /// takes Esc to shut the menu, and the keydown would bubble on up and shut the dialog too.</summary>
    [Parameter] public bool CloseOnEscape { get; set; }
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

    // No handler at all unless asked for, so typing in an edit dialog never crosses the circuit.
    private EventCallback<KeyboardEventArgs> EscapeHandler
        => CloseOnEscape ? EventCallback.Factory.Create<KeyboardEventArgs>(this, OnKeyDownAsync) : default;

    private async Task OnKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
            await CloseAsync();
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
