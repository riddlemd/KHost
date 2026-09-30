using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

public partial class HelpDialog
{
    private const string _rootClassName = "kh-help-dialog";

    // A reopen remounts <details> from nothing (Scrim's @if tears the whole body down), so restoring
    // an already-expanded section means setting open=true on a brand-new element — which the browser
    // treats as a real state change and fires its own toggle for. Left unswallowed, that toggle flips
    // the state straight back off, which flips the attribute, which fires another toggle: a flicker
    // loop, not a one-time glitch. Each flag eats exactly that one synthetic toggle per mount.
    private bool _suppressQuickGuideToggle;
    private bool _suppressShortcutsToggle;

    [Inject] private IControlState ControlState { get; set; } = default!;

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public bool CloseOnScrimClick { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

    private static KeyboardShortcutGroup[] Shortcuts => KeyboardShortcuts.All;

    protected override void OnInitialized()
    {
        _suppressQuickGuideToggle = ControlState.HelpQuickGuideExpanded;
        _suppressShortcutsToggle = ControlState.HelpShortcutsExpanded;
    }

    private void ToggleQuickGuide()
    {
        if (_suppressQuickGuideToggle) { _suppressQuickGuideToggle = false; return; }
        ControlState.HelpQuickGuideExpanded = !ControlState.HelpQuickGuideExpanded;
    }

    private void ToggleShortcuts()
    {
        if (_suppressShortcutsToggle) { _suppressShortcutsToggle = false; return; }
        ControlState.HelpShortcutsExpanded = !ControlState.HelpShortcutsExpanded;
    }

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
