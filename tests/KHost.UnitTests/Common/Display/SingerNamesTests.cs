using KHost.Abstractions.Models;
using KHost.Common.Display;

namespace KHost.UnitTests.Common.Display;

public class SingerNamesTests
{
    private static readonly KHostUser Singer = new() { Name = "Ada" };

    [Fact]
    public void DisplayedFor_AliasesAllowed_UsesTheRecordedName()
        => Assert.Equal("Lady A", SingerNames.DisplayedFor(new Performance { SungAs = "Lady A" }, Singer, true));

    [Fact]
    public void DisplayedFor_AliasesAllowed_TrimsTheRecordedName()
        => Assert.Equal("Lady A", SingerNames.DisplayedFor(new Performance { SungAs = "  Lady A " }, Singer, true));

    [Fact]
    public void DisplayedFor_AliasesNotAllowed_UsesTheSingersName()
        => Assert.Equal("Ada", SingerNames.DisplayedFor(new Performance { SungAs = "Lady A" }, Singer, false));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DisplayedFor_NothingRecorded_UsesTheSingersName(string? recorded)
        => Assert.Equal("Ada", SingerNames.DisplayedFor(new Performance { SungAs = recorded }, Singer, true));

    [Fact]
    public void DisplayedFor_NoTurn_UsesTheSingersName()
        => Assert.Equal("Ada", SingerNames.DisplayedFor(null, Singer, true));
}
