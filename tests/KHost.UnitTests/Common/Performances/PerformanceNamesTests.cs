using KHost.Abstractions.Models;
using KHost.Common.Performances;

namespace KHost.UnitTests.Common.Performances;

/// <summary>
/// One rule, because every surface that shows a singer needs the same answer and the venue has a
/// say in it — a copy per surface is a copy that forgets the venue.
/// </summary>
public class PerformanceNamesTests
{
    private static readonly KHostUser Singer = new() { Id = Guid.NewGuid(), Name = "Priya" };

    [Fact]
    public void DisplayName_ARecordedNameAndAliasesAllowed_ShowsWhatWasRecorded()
        => Assert.Equal("DJ P", Sung("DJ P").DisplayName(Singer, aliasesAllowed: true));

    [Fact]
    public void DisplayName_ARecordedNameAndAliasesRefused_ShowsTheSingerTheVenueKnows()
    {
        // The venue's call, and the name stays recorded either way — turning the setting on later
        // shows what was already there rather than starting from nothing.
        Assert.Equal("Priya", Sung("DJ P").DisplayName(Singer, aliasesAllowed: false));
    }

    [Fact]
    public void DisplayName_NothingRecorded_FallsBackToTheSinger()
        => Assert.Equal("Priya", Sung(null).DisplayName(Singer, aliasesAllowed: true));

    [Fact]
    public void DisplayName_NothingRecordedAndTheSingerIsGone_SaysSoRatherThanNothing()
        => Assert.Equal(PerformanceNames.UnknownSinger, Sung(null).DisplayName(null, aliasesAllowed: true));

    [Fact]
    public void DisplayName_TheSingerIsGoneButTheNameWasRecorded_StillNamesThem()
    {
        // The reason the name is kept on the row at all: a sung performance outlives the singer,
        // and deleting someone must not turn their history anonymous.
        Assert.Equal("DJ P", Sung("DJ P").DisplayName(null, aliasesAllowed: false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DisplayName_ARecordedNameOfNothing_IsNotAName(string recorded)
        => Assert.Equal("Priya", Sung(recorded).DisplayName(Singer, aliasesAllowed: true));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DisplayName_ASingerRowWithNoNameOnIt_IsNotAName(string blank)
    {
        // Distinct from the singer being gone: the row is right there, and reading its empty name
        // as a name puts a blank where the screen should say it does not know.
        var nameless = new KHostUser { Id = Guid.NewGuid(), Name = blank };

        Assert.Equal(PerformanceNames.UnknownSinger, Sung(null).DisplayName(nameless, aliasesAllowed: true));
        Assert.Equal("DJ P", Sung("DJ P").DisplayName(nameless, aliasesAllowed: false));
    }

    private static Performance Sung(string? sungAs) => new()
    {
        Id = Guid.NewGuid(),
        SingerId = Singer.Id,
        MediaId = Guid.NewGuid(),
        SungAs = sungAs,
    };
}
