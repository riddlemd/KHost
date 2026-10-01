namespace KHost.UserInterface.Models;

public sealed record KeyboardShortcut(string[] Keys, string Description);

public sealed record KeyboardShortcutGroup(string Title, KeyboardShortcut[] Shortcuts, string? Note = null);

/// <summary>What the shortcuts dialog lists; a new shortcut has to be added here too.</summary>
/// <remarks>This dialog is the only place a host discovers one, so a missing entry is invisible.</remarks>
public static class KeyboardShortcuts
{
    // Ctrl reads better in a hint than a platform switch would, and shortcuts.js takes either.
    private const string Accel = "ctrl";

    public static readonly KeyboardShortcutGroup[] All =
    [
        new("General",
        [
            new(["?"], "Open help and keyboard shortcuts"),
        ]),

        new("Panels",
        [
            new([Accel, "1"], "Focus the new singer's name field"),
            new([Accel, "2"], "Focus song search"),
            new([Accel, "3"], "Focus the singer queue"),
            new([Accel, "4"], "Focus the selected singer's songs"),
        ],
        "Cmd works as well as Ctrl. Ctrl+3 needs a singer in the queue, Ctrl+4 a selected singer."),

        new("Now playing",
        [
            new([Accel, "enter"], "Play the top singer's next song"),
            new([Accel, "shift", "enter"], "Announce the next singer"),
            new([Accel, "alt", "enter"], "Pause or resume the song"),
            new([Accel, "backspace"], "Stop the song"),
        ],
        "Cmd works as well as Ctrl (Cmd+Option+Enter on a Mac). Outside text fields, where these keys "
        + "edit text instead. Does nothing while its button would be disabled or hidden."),

        new("Singer queue",
        [
            new(["↑"], "Select the singer above"),
            new(["↓"], "Select the singer below"),
            new(["shift", "↑"], "Move the selected singer up the queue"),
            new(["shift", "↓"], "Move the selected singer down the queue"),
            new(["delete"], "Remove the selected singer (backspace too)"),
        ],
        "Click a singer first, or press Ctrl+3. The keys act on the list holding focus; removing asks "
        + "first when the venue says to."),

        new("Singer's songs",
        [
            new(["↑"], "Select the song above"),
            new(["↓"], "Select the song below"),
            new(["shift", "↑"], "Move the selected song up the queue"),
            new(["shift", "↓"], "Move the selected song down the queue"),
            new(["delete"], "Remove the selected song (backspace too)"),
        ],
        "Click a song first, for the same reason. Removing asks first when the venue says to, "
        + "and the singer or song on stage cannot be removed."),

        new("Song search",
        [
            new(["↓"], "From the search box, move into the results"),
            new(["↑"], "Select the result above; from the first, back to the search box"),
            new(["enter"], "Queue the selected result for the selected singer"),
            new(["alt", "←"], "In the search box, switch to the previous search source"),
            new(["alt", "→"], "In the search box, switch to the next search source"),
        ],
        "Switching source does not search; press Enter to run it. Option works as Alt on a Mac."),
    ];
}
