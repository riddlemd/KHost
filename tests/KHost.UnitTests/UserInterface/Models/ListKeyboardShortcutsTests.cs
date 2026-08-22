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
}
