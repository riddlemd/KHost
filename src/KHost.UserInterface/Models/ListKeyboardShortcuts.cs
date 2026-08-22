namespace KHost.UserInterface.Models;

/// <summary>What an arrow-key press means to a panel showing an ordered list.</summary>
public enum ListKeyAction
{
    None,
    SelectPrevious,
    SelectNext,
    MovePrevious,
    MoveNext
}

/// <summary>
/// The arrow-key rules shared by the singer queue and a singer's song queue: plain moves the
/// selection, Shift moves the selected row itself.
/// </summary>
public static class ListKeyboardShortcuts
{
    /// <param name="currentIndex">Index of the selected row, or -1 when nothing is selected.</param>
    public static ListKeyAction Resolve(string? key, bool shift, int currentIndex, int count)
    {
        var up = key == "ArrowUp";
        var down = key == "ArrowDown";

        if (count <= 0 || (!up && !down))
            return ListKeyAction.None;

        // Nothing selected: Down picks the first row, and there is nothing to reorder.
        if (shift && currentIndex < 0)
            return ListKeyAction.None;

        if (up)
        {
            if (currentIndex <= 0) return ListKeyAction.None;

            return shift ? ListKeyAction.MovePrevious : ListKeyAction.SelectPrevious;
        }

        if (currentIndex >= count - 1) return ListKeyAction.None;

        return shift ? ListKeyAction.MoveNext : ListKeyAction.SelectNext;
    }
}
