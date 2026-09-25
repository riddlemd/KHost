using KHost.Abstractions.Models;
using KHost.Common.Display;

namespace KHost.UnitTests.Common.Display;

/// <summary>The wording rules any display drawing a venue's marquee shares with the local screen.</summary>
public class MarqueeTextTests
{
    private static UpNextEntry Entry(string singer, string? title = null, string? artist = null, int position = 1)
        => new() { Position = position, Singer = singer, Title = title, Artist = artist };

    [Fact]
    public void ComposeEntry_NoFormat_ReadsSongThenSinger()
        => Assert.Equal("Africa - Ada", MarqueeText.ComposeEntry(Entry("Ada", "Africa"), null));

    [Fact]
    public void ComposeEntry_EveryTag_IsReplacedWithoutRegardToCase()
        => Assert.Equal(
            "2. Africa by Toto (Ada)",
            MarqueeText.ComposeEntry(Entry("Ada", "Africa", "Toto", position: 2), "{Position}. {SONG} by {artist} ({singer})"));

    /// <summary>A format naming a song would promise one a singer with nothing queued does not have.</summary>
    [Fact]
    public void ComposeEntry_NothingQueued_NamesTheSingerAlone()
        => Assert.Equal("Ada", MarqueeText.ComposeEntry(Entry("Ada"), "{song} - {singer}"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ComposeEntry_BlankFormat_UsesTheDefault(string blank)
        => Assert.Equal("Africa - Ada", MarqueeText.ComposeEntry(Entry("Ada", "Africa"), blank));

    [Fact]
    public void ComposeEntry_NoArtist_LeavesThatTagEmpty()
        => Assert.Equal("Africa / ", MarqueeText.ComposeEntry(Entry("Ada", "Africa"), "{song} / {artist}"));

    [Fact]
    public void CollapseToOneLine_KeepsTheWordsAndLosesTheShape()
        => Assert.Equal("Happy hour until 8 ask", MarqueeText.CollapseToOneLine("  Happy hour\n\nuntil 8\task  "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n ")]
    public void CollapseToOneLine_Blank_IsNoMessage(string? blank)
        => Assert.Null(MarqueeText.CollapseToOneLine(blank));
}
