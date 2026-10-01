namespace KHost.UserInterface.Models;

/// <summary>What a key press means to a panel showing an ordered list.</summary>
public enum ListKeyAction
{
    None,
    SelectPrevious,
    SelectNext,
    MovePrevious,
    MoveNext,
    Remove
}

/// <summary>Key rules shared by the singer queue, a singer's song queue and the search results.</summary>
/// <remarks>Plain arrows move the selection; Shift moves the selected row itself; Delete or a plain
/// Backspace removes it.</remarks>
public static class ListKeyboardShortcuts
{
    /// <param name="currentIndex">Index of the selected row, or -1 when nothing is selected.</param>
    /// <param name="modified">Ctrl, Cmd or Alt is held. Mod+Backspace is the global stop chord and
    /// deletes a word in a text field, so a modified Backspace or Delete never removes a row.</param>
    public static ListKeyAction Resolve(string? key, bool shift, int currentIndex, int count, bool modified = false)
    {
        if (count <= 0)
            return ListKeyAction.None;

        // A Mac has no forward Delete without Fn, so its "delete" key (Backspace) has to work too.
        if (key is "Delete" or "Backspace")
            return !shift && !modified && currentIndex >= 0 && currentIndex < count
                ? ListKeyAction.Remove
                : ListKeyAction.None;

        var up = key == "ArrowUp";
        var down = key == "ArrowDown";

        if (!up && !down)
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

    /// <summary>The row to select once the row at <paramref name="removedIndex"/> is gone, as an
    /// index into the list <em>before</em> the removal: the row below, else the row above, else -1.</summary>
    public static int NeighbourAfterRemoval(int removedIndex, int count)
    {
        if (removedIndex < 0 || removedIndex >= count) return -1;
        if (removedIndex + 1 < count) return removedIndex + 1;

        return removedIndex - 1;
    }

    /// <summary>Runs what <see cref="Resolve"/> decided: <paramref name="select"/> is given the row
    /// to select, <paramref name="move"/> is told which way to move the current row. Reordering is
    /// a permission of its own, so a Move action is dropped rather than handed to the caller when
    /// <paramref name="canReorder"/> is false — the arrows that do it are hidden without it.
    /// <paramref name="remove"/> must go through the same confirm and refusals as the row's own
    /// remove button; a null one means the list has none, and the key does nothing.</summary>
    public static async Task DispatchAsync(
        ListKeyAction action, int currentIndex, bool canReorder, Func<int, Task> select, Func<bool, Task> move,
        Func<Task>? remove = null)
    {
        if (action is ListKeyAction.MovePrevious or ListKeyAction.MoveNext && !canReorder)
            return;

        switch (action)
        {
            case ListKeyAction.SelectPrevious:
                await select(currentIndex - 1);
                break;
            case ListKeyAction.SelectNext:
                await select(currentIndex + 1);
                break;
            case ListKeyAction.MovePrevious:
                await move(true);
                break;
            case ListKeyAction.MoveNext:
                await move(false);
                break;
            case ListKeyAction.Remove when remove is not null:
                await remove();
                break;
        }
    }
}
