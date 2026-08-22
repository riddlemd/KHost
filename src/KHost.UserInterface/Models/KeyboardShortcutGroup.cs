namespace KHost.UserInterface.Models;

public sealed record KeyboardShortcut(string[] Keys, string Description);

public sealed record KeyboardShortcutGroup(string Title, KeyboardShortcut[] Shortcuts, string? Note = null);

/// <summary>
/// What the shortcuts dialog lists. Adding a shortcut means adding it here too — the dialog is
/// the only place a host can find out these exist, so one that is missing might as well not be
/// implemented.
/// </summary>
public static class KeyboardShortcuts
{
    // Ctrl reads better in a hint than a platform switch would, and shortcuts.js takes either.
    private const string Accel = "ctrl";

    public static readonly KeyboardShortcutGroup[] All =
    [
        new("Panels",
        [
            new([Accel, "1"], "Focus the new singer's name field"),
            new([Accel, "2"], "Focus media search"),
        ],
        "Cmd works as well as Ctrl."),

        new("Singer queue",
        [
            new(["↑"], "Select the singer above"),
            new(["↓"], "Select the singer below"),
            new(["shift", "↑"], "Move the selected singer up the queue"),
            new(["shift", "↓"], "Move the selected singer down the queue"),
        ],
        "Click a singer first — the arrows act on the list holding focus."),

        new("Singer's songs",
        [
            new(["↑"], "Select the song above"),
            new(["↓"], "Select the song below"),
            new(["shift", "↑"], "Move the selected song up the queue"),
            new(["shift", "↓"], "Move the selected song down the queue"),
        ],
        "Click a song first, for the same reason."),
    ];
}
