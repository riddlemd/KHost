using KHost.Abstractions.Models;
using KHost.Common.Display;
using KHost.Domain.Services.Displays.LocalScreen;
using KHost.IPC.SignalR.Contracts;

namespace KHost.UnitTests.Domain.Services.Displays.LocalScreen;

/// <summary>One upcoming turn split into coloured runs that read exactly as the plain marquee text.</summary>
public class MarqueeEntrySegmenterTests
{
    private static readonly UpNextEntry Turn = new() { Position = 2, Singer = "Ann", Title = "Jolene", Artist = "Dolly Parton" };

    [Fact]
    public void ComposeSegments_NothingQueued_IsTheSingerAloneWhateverTheFormat()
        => Assert.Equal(
            [("Ann", MarqueeSegmentKind.Singer)],
            Runs(Turn with { Title = null, Artist = null }, "{position}. {song} by {artist}"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ComposeSegments_NoFormat_UsesTheDefaultSongThenSinger(string? format)
        => Assert.Equal(
            [("Jolene", MarqueeSegmentKind.Song), (" - ", MarqueeSegmentKind.Other), ("Ann", MarqueeSegmentKind.Singer)],
            Runs(Turn, format));

    /// <summary>Tags are found in whatever order and case the venue wrote them.</summary>
    [Fact]
    public void ComposeSegments_TagsInAnyOrderAndCase_AreRunsInReadingOrder()
        => Assert.Equal(
            [
                ("Ann", MarqueeSegmentKind.Singer),
                (" #", MarqueeSegmentKind.Other),
                ("2", MarqueeSegmentKind.Other),
                (": ", MarqueeSegmentKind.Other),
                ("Jolene", MarqueeSegmentKind.Song),
                (" by ", MarqueeSegmentKind.Other),
                ("Dolly Parton", MarqueeSegmentKind.Song),
            ],
            Runs(Turn, "{SINGER} #{Position}: {song} by {Artist}"));

    [Fact]
    public void ComposeSegments_NoArtistRecorded_DrawsNoEmptyRun()
        => Assert.Equal(
            [("Jolene", MarqueeSegmentKind.Song), (" (", MarqueeSegmentKind.Other), (")", MarqueeSegmentKind.Other)],
            Runs(Turn with { Artist = null }, "{song} ({artist})"));

    [Fact]
    public void ComposeSegments_FormatWithNoTags_IsOneRunOfItsText()
        => Assert.Equal([("Up next {unknown}", MarqueeSegmentKind.Other)], Runs(Turn, "Up next {unknown}"));

    /// <summary>A tag at the very start or end leaves no empty run beside it.</summary>
    [Fact]
    public void ComposeSegments_TagsBackToBack_AreOnlyTheirValues()
        => Assert.Equal(
            [("Jolene", MarqueeSegmentKind.Song), ("Ann", MarqueeSegmentKind.Singer)],
            Runs(Turn, "{song}{singer}"));

    [Fact]
    public void ComposeSegments_VeryLongName_IsKeptWholeInOneRun()
    {
        var name = new string('x', 5000);

        var runs = Runs(Turn with { Singer = name }, "{singer}");

        Assert.Equal([(name, MarqueeSegmentKind.Singer)], runs);
    }

    /// <summary>The coloured runs and the plain wording can only ever differ in colour.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("{position}. {singer}: {song} - {artist}")]
    [InlineData("{ARTIST} / {Song} ({singer})")]
    [InlineData("no tags at all")]
    public void ComposeSegments_ReadTogether_AreTheMarqueeText(string? format)
    {
        foreach (var turn in new[] { Turn, Turn with { Artist = null }, Turn with { Title = null, Artist = null } })
        {
            Assert.Equal(
                MarqueeText.ComposeEntry(turn, format),
                string.Concat(MarqueeEntrySegmenter.ComposeSegments(turn, format).Select(s => s.Text)));
        }
    }

    [Theory]
    [InlineData(MarqueeDividerShape.Dot, "•")]
    [InlineData(MarqueeDividerShape.Diamond, "◆")]
    [InlineData(MarqueeDividerShape.Star, "★")]
    [InlineData(MarqueeDividerShape.Slash, "/")]
    [InlineData(MarqueeDividerShape.Pipe, "|")]
    [InlineData(MarqueeDividerShape.Note, "♪")]
    [InlineData(MarqueeDividerShape.None, null)]
    public void ResolveGlyph_EachShape_IsItsGlyph(MarqueeDividerShape shape, string? glyph)
        => Assert.Equal(glyph, MarqueeEntrySegmenter.ResolveGlyph(shape));

    private static List<(string Text, MarqueeSegmentKind Kind)> Runs(UpNextEntry entry, string? format)
        => MarqueeEntrySegmenter.ComposeSegments(entry, format).Select(s => (s.Text, s.Kind)).ToList();
}
