using KHost.Domain.Collections.Generics;

namespace KHost.UnitTests.Domain.Collections.Generics;

public class ListExtensionsTests
{
    [Fact]
    public void FindIndex_ReturnsIndex_OfFirstMatch()
    {
        IList<int> list = new List<int> { 10, 20, 30, 40 };

        var index = list.FindIndex(x => x == 30);

        Assert.Equal(2, index);
    }

    [Fact]
    public void FindIndex_ReturnsMinusOne_WhenNoMatch()
    {
        IList<int> list = new List<int> { 10, 20, 30 };

        var index = list.FindIndex(x => x == 99);

        Assert.Equal(-1, index);
    }

    [Fact]
    public void FindIndex_ReturnsFirst_WhenMultipleMatches()
    {
        IList<string> list = new List<string> { "a", "b", "b", "c" };

        var index = list.FindIndex(x => x == "b");

        Assert.Equal(1, index);
    }

    [Fact]
    public void FindIndex_ReturnsMinusOne_WhenListIsEmpty()
    {
        IList<int> list = new List<int>();

        var index = list.FindIndex(_ => true);

        Assert.Equal(-1, index);
    }

    [Fact]
    public void FindIndex_ReturnsZero_WhenFirstElementMatches()
    {
        IList<int> list = new List<int> { 5, 6, 7 };

        var index = list.FindIndex(x => x == 5);

        Assert.Equal(0, index);
    }

    [Fact]
    public void FindIndex_WorksWithArrayBackedList()
    {
        IList<int> list = new int[] { 1, 2, 3 };

        var index = list.FindIndex(x => x == 3);

        Assert.Equal(2, index);
    }
}
