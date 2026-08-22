using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

public partial class ShortcutsDialog
{
    private const string _rootClassName = "kh-shortcuts-dialog";

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public bool CloseOnScrimClick { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

    private static KeyboardShortcutGroup[] Shortcuts => KeyboardShortcuts.All;

    public async Task CloseAsync()
    {
        IsOpen = false;

        await OnClose.InvokeAsync();
    }

    public record DialogRequest(Action? OnClose) : BaseDialogRequest(OnClose);
}
