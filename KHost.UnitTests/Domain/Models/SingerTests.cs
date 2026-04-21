using KHost.Abstractions.Models;

namespace KHost.UnitTests.Domain.Models;

public class SingerTests
{
    [Fact]
    public void Singer_InitializesWithNewGuidId()
    {
        var singer = new Singer { Name = "Alice" };

        Assert.NotEqual(Guid.Empty, singer.Id);
    }

    [Fact]
    public void Singer_TwoInstancesHaveDistinctIds()
    {
        var a = new Singer { Name = "Alice" };
        var b = new Singer { Name = "Bob" };

        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void Singer_DefaultIsTipperIsFalse()
    {
        var singer = new Singer { Name = "Alice" };

        Assert.False(singer.IsTipper);
    }

    [Fact]
    public void Singer_DefaultIsRegularIsFalse()
    {
        var singer = new Singer { Name = "Alice" };

        Assert.False(singer.IsRegular);
    }

    [Fact]
    public void Singer_NotesDefaultsToEmptyString()
    {
        var singer = new Singer { Name = "Alice" };

        Assert.Equal("", singer.Notes);
    }
}
