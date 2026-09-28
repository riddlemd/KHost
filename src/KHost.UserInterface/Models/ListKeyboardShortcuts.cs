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

/// <summary>Arrow-key rules shared by the singer queue and a singer's song queue.</summary>
/// <remarks>Plain moves the selection; Shift moves the selected row itself.</remarks>
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

    /// <summary>Runs what <see cref="Resolve"/> decided: <paramref name="select"/> is given the row
    /// to select, <paramref name="move"/> is told which way to move the current row. Reordering is
    /// a permission of its own, so a Move action is dropped rather than handed to the caller when
    /// <paramref name="canReorder"/> is false — the arrows that do it are hidden without it.</summary>
    public static async Task DispatchAsync(
        ListKeyAction action, int currentIndex, bool canReorder, Func<int, Task> select, Func<bool, Task> move)
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
        }
    }
}
