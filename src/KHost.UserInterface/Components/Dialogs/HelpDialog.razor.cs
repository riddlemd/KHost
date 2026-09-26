using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

public partial class HelpDialog
{
    private const string _rootClassName = "kh-help-dialog";

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public bool CloseOnScrimClick { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

    private static KeyboardShortcutGroup[] Shortcuts => KeyboardShortcuts.All;

    // Operational only: what running a night looks like, not what the app is or how it is licensed
    // — that stays in the About page. Checked against the actual panels rather than aspired-to ones.
    private static readonly string[] QuickGuide =
    [
        "Add a singer: type their name in the Singer Queue panel and press + (Ctrl/Cmd+1 jumps to the field).",
        "The queue is the rotation — the singer at the top sings next. Drag a row, or select it and press Shift+Up/Down, to reorder.",
        "Select a singer, then search for their song in Song Search (Ctrl/Cmd+2) and add it to their queue.",
        "Press the play button beside a queued song to load and play it; pause, stop and skip live in Now Playing.",
        "Pick a screen or receiver from the menu's Display section to send video and audio out.",
        "Break music plays on its own between singers; its controls sit in the band above Now Playing.",
    ];

    public async Task CloseAsync()
    {
        IsOpen = false;

        await OnClose.InvokeAsync();
    }

    public record DialogRequest(Action? OnClose) : BaseDialogRequest(OnClose);
}
