using KHost.UserInterface.Models;

namespace KHost.UnitTests.UserInterface.Models;

public class ListKeyboardShortcutsTests
{
    [Theory]
    [InlineData("ArrowUp", 2, ListKeyAction.SelectPrevious)]
    [InlineData("ArrowDown", 2, ListKeyAction.SelectNext)]
    public void Resolve_MovesTheSelection_WithoutShift(string key, int currentIndex, ListKeyAction expected)
        => Assert.Equal(expected, ListKeyboardShortcuts.Resolve(key, shift: false, currentIndex, count: 5));

    [Theory]
    [InlineData("ArrowUp", 2, ListKeyAction.MovePrevious)]
    [InlineData("ArrowDown", 2, ListKeyAction.MoveNext)]
    public void Resolve_MovesTheRow_WithShift(string key, int currentIndex, ListKeyAction expected)
        => Assert.Equal(expected, ListKeyboardShortcuts.Resolve(key, shift: true, currentIndex, count: 5));

    [Theory]
    [InlineData("ArrowUp", 0)]
    [InlineData("ArrowDown", 4)]
    public void Resolve_StopsAtTheEnds(string key, int currentIndex)
    {
        Assert.Equal(ListKeyAction.None, ListKeyboardShortcuts.Resolve(key, shift: false, currentIndex, count: 5));
        Assert.Equal(ListKeyAction.None, ListKeyboardShortcuts.Resolve(key, shift: true, currentIndex, count: 5));
    }

    // The selection starts unset, and Down is how a host reaches the first row.
    [Fact]
    public void Resolve_SelectsTheFirstRow_WhenNothingIsSelectedYet()
        => Assert.Equal(ListKeyAction.SelectNext, ListKeyboardShortcuts.Resolve("ArrowDown", shift: false, currentIndex: -1, count: 5));

    [Theory]
    [InlineData("ArrowUp")]
    [InlineData("ArrowDown")]
    public void Resolve_RefusesToReorder_WhenNothingIsSelected(string key)
        => Assert.Equal(ListKeyAction.None, ListKeyboardShortcuts.Resolve(key, shift: true, currentIndex: -1, count: 5));

    [Theory]
    [InlineData("Enter")]
    [InlineData("ArrowLeft")]
    [InlineData("a")]
    [InlineData(null)]
    public void Resolve_IgnoresEveryOtherKey(string? key)
        => Assert.Equal(ListKeyAction.None, ListKeyboardShortcuts.Resolve(key, shift: false, currentIndex: 2, count: 5));

    [Fact]
    public void Resolve_IgnoresAnEmptyList()
        => Assert.Equal(ListKeyAction.None, ListKeyboardShortcuts.Resolve("ArrowDown", shift: false, currentIndex: -1, count: 0));

    [Fact]
    public async Task DispatchAsync_SelectPrevious_SelectsTheRowBeforeTheCurrentOne()
    {
        int? selected = null;

        await ListKeyboardShortcuts.DispatchAsync(
            ListKeyAction.SelectPrevious, currentIndex: 2, canReorder: true,
            select: idx => { selected = idx; return Task.CompletedTask; },
            move: _ => throw new InvalidOperationException("Select must not move."));

        Assert.Equal(1, selected);
    }

    [Fact]
    public async Task DispatchAsync_SelectNext_SelectsTheRowAfterTheCurrentOne()
    {
        int? selected = null;

        await ListKeyboardShortcuts.DispatchAsync(
            ListKeyAction.SelectNext, currentIndex: 2, canReorder: true,
            select: idx => { selected = idx; return Task.CompletedTask; },
            move: _ => throw new InvalidOperationException("Select must not move."));

        Assert.Equal(3, selected);
    }

    [Theory]
    [InlineData(ListKeyAction.MovePrevious, true)]
    [InlineData(ListKeyAction.MoveNext, false)]
    public async Task DispatchAsync_Move_TellsTheCallerWhichDirection(ListKeyAction action, bool expectedUp)
    {
        bool? up = null;

        await ListKeyboardShortcuts.DispatchAsync(
            action, currentIndex: 2, canReorder: true,
            select: _ => throw new InvalidOperationException("Move must not select."),
            move: isUp => { up = isUp; return Task.CompletedTask; });

        Assert.Equal(expectedUp, up);
    }

    [Theory]
    [InlineData(ListKeyAction.MovePrevious)]
    [InlineData(ListKeyAction.MoveNext)]
    public async Task DispatchAsync_Move_DoesNothing_WithoutReorderPermission(ListKeyAction action)
    {
        var moved = false;

        await ListKeyboardShortcuts.DispatchAsync(
            action, currentIndex: 2, canReorder: false,
            select: _ => throw new InvalidOperationException("Move must not select."),
            move: _ => { moved = true; return Task.CompletedTask; });

        Assert.False(moved);
    }

    [Fact]
    public async Task DispatchAsync_None_CallsNeitherDelegate()
    {
        await ListKeyboardShortcuts.DispatchAsync(
            ListKeyAction.None, currentIndex: 2, canReorder: true,
            select: _ => throw new InvalidOperationException("None must not select."),
            move: _ => throw new InvalidOperationException("None must not move."));
    }
    [Theory]
    [InlineData("Delete")]
    [InlineData("Backspace")]
    public void Resolve_RemovesTheSelectedRow_OnDeleteOrBackspace(string key)
        => Assert.Equal(ListKeyAction.Remove, ListKeyboardShortcuts.Resolve(key, shift: false, currentIndex: 4, count: 5));

    // Mod+Backspace is the stop chord, and Alt+Backspace deletes a word: neither removes a row.
    [Theory]
    [InlineData("Backspace", false, true)]
    [InlineData("Delete", false, true)]
    [InlineData("Backspace", true, false)]
    public void Resolve_DoesNotRemove_WithAModifierHeld(string key, bool shift, bool modified)
        => Assert.Equal(ListKeyAction.None, ListKeyboardShortcuts.Resolve(key, shift, currentIndex: 2, count: 5, modified));

    [Fact]
    public void Resolve_DoesNotRemove_WhenNothingIsSelected()
        => Assert.Equal(ListKeyAction.None, ListKeyboardShortcuts.Resolve("Delete", shift: false, currentIndex: -1, count: 5));

    [Fact]
    public void Resolve_DoesNotRemove_FromAnEmptyList()
        => Assert.Equal(ListKeyAction.None, ListKeyboardShortcuts.Resolve("Delete", shift: false, currentIndex: 0, count: 0));

    [Theory]
    [InlineData(0, 3, 1)]
    [InlineData(1, 3, 2)]
    [InlineData(2, 3, 1)]
    [InlineData(0, 1, -1)]
    [InlineData(-1, 3, -1)]
    [InlineData(3, 3, -1)]
    public void NeighbourAfterRemoval_PrefersTheRowBelow(int removed, int count, int expected)
        => Assert.Equal(expected, ListKeyboardShortcuts.NeighbourAfterRemoval(removed, count));

    [Fact]
    public async Task DispatchAsync_Remove_CallsRemove()
    {
        var removed = false;

        await ListKeyboardShortcuts.DispatchAsync(
            ListKeyAction.Remove, currentIndex: 2, canReorder: false,
            select: _ => throw new InvalidOperationException("Remove must not select."),
            move: _ => throw new InvalidOperationException("Remove must not move."),
            remove: () => { removed = true; return Task.CompletedTask; });

        Assert.True(removed);
    }

    [Fact]
    public async Task DispatchAsync_Remove_WithNoRemover_DoesNothing()
        => await ListKeyboardShortcuts.DispatchAsync(
            ListKeyAction.Remove, currentIndex: 2, canReorder: true,
            select: _ => throw new InvalidOperationException("Remove must not select."),
            move: _ => throw new InvalidOperationException("Remove must not move."));
}
