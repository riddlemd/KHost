using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.Displays.LocalScreen;
using KHost.IPC.SignalR.Contracts;
using KHost.Abstractions.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services.Displays.LocalScreen;

/// <summary>What the local screen's marquee says, composed by its provider from the venue and who is next.</summary>
public class LocalScreenMarqueeTests
{
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();

    // Through the real up-next list, which is where the singers the band names come from.
    private async Task<SetMarqueeCommand> BuildAsync()
        => await LocalScreenDisplayProvider.BuildMarqueeAsync(
            (await _venues.ReadSelectedVenueAsync())?.Settings,
            new UpNextService(
                NullLogger<UpNextService>.Instance, Substitute.For<IMessageBroker>(), _venues, _queue, _performances, _media,
                new ServiceCollection().AddSingleton(_playback).BuildServiceProvider()));

    public LocalScreenMarqueeTests()
        // NSubstitute hands back a task wrapping null otherwise, and the composition .Where()s it.
        => _performances.ReadQueuedAsync().Returns([]);

    [Fact]
    public async Task BuildMarqueeAsync_NoVenueSelected_IsDisabled()
    {
        _venues.ReadSelectedVenueAsync().Returns((Venue?)null);

        Assert.False((await BuildAsync()).Enabled);
    }

    [Fact]
    public async Task BuildMarqueeAsync_VenueHasMarqueeOff_IsDisabled()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = false, MarqueeMessage = "Ignored" });

        Assert.False((await BuildAsync()).Enabled);
    }

    [Fact]
    public async Task BuildMarqueeAsync_MarqueeOn_TakesOnlyTheVenuesSingerCountInQueueOrder()
    {
        Arrange(
            new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 2 },
            Singer("Ada"), Singer("Grace"), Singer("Linus"));

        var command = await BuildAsync();

        Assert.Equal(["Ada", "Grace"], Turns(command));
    }

    /// <summary>The room is looking for the song as much as the name, so the band leads with it.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_SingerHasASongQueued_ReadsSongThenSinger()
    {
        var ada = Singer("Ada");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1 }, ada);
        Queued(ada, "Bohemian Rhapsody");

        Assert.Equal(["Bohemian Rhapsody - Ada"], Turns(await BuildAsync()));
    }

    /// <summary>A host's own wording replaces "{song} - {singer}", tag for tag.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_CustomEntryFormat_UsesTheVenuesWording()
    {
        var ada = Singer("Ada");
        Arrange(
            new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1, MarqueeEntryFormat = "{artist} - {song}" },
            ada);
        Queued(ada, "Bohemian Rhapsody", "Queen");

        Assert.Equal(["Queen - Bohemian Rhapsody"], Turns(await BuildAsync()));
    }

    /// <summary>Numbering starts at one, matching how a host would read the list aloud.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_EntryFormatUsesPosition_NumbersFromOne()
    {
        var ada = Singer("Ada");
        var grace = Singer("Grace");
        Arrange(
            new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 2, MarqueeEntryFormat = "{position}. {song} - {singer}" },
            ada, grace);
        Queued(ada, "Africa");
        Queued(grace, "Wonderwall");

        Assert.Equal(["1. Africa - Ada", "2. Wonderwall - Grace"], Turns(await BuildAsync()));
    }

    /// <summary>Tags read the same regardless of how a host capitalises them while typing.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_EntryFormatTagsAreCaseInsensitive()
    {
        var ada = Singer("Ada");
        Arrange(
            new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1, MarqueeEntryFormat = "{SONG} by {Singer}" },
            ada);
        Queued(ada, "Africa");

        Assert.Equal(["Africa by Ada"], Turns(await BuildAsync()));
    }

    /// <summary>A blank format would compose empty lines, so it reads as unset.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BuildMarqueeAsync_BlankEntryFormat_FallsBackToTheDefault(string blank)
    {
        var ada = Singer("Ada");
        Arrange(
            new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1, MarqueeEntryFormat = blank },
            ada);
        Queued(ada, "Bohemian Rhapsody");

        Assert.Equal(["Bohemian Rhapsody - Ada"], Turns(await BuildAsync()));
    }

    /// <summary>A singer with nothing queued must still show, or the band disagrees with the queue.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_SingerHasNoSongQueued_NamesThemAlone()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1 }, Singer("Ada"));

        Assert.Equal(["Ada"], Turns(await BuildAsync()));
    }

    /// <summary>Each singer gets their own song, not the first one in the queue.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_SeveralSingers_PairsEachWithTheirOwnSong()
    {
        var ada = Singer("Ada");
        var grace = Singer("Grace");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 2 }, ada, grace);
        Queued(ada, "Africa");
        Queued(grace, "Wonderwall");

        Assert.Equal(["Africa - Ada", "Wonderwall - Grace"], Turns(await BuildAsync()));
    }

    /// <summary>A media row that has lost its title must not read as " - Ada".</summary>
    [Fact]
    public async Task BuildMarqueeAsync_QueuedSongHasNoTitle_NamesTheSingerAlone()
    {
        var ada = Singer("Ada");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1 }, ada);
        Queued(ada, "   ");

        Assert.Equal(["Ada"], Turns(await BuildAsync()));
    }

    /// <summary>The band is one line; a pasted message keeps its words and loses its shape.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_MessageSpansLines_ArrivesAsOneLine()
    {
        Arrange(new Venue.VenueSettings
        {
            MarqueeEnabled = true,
            MarqueeMessage = "Happy hour until 8\n\nask your host   about specials",
        });

        Assert.Equal("Happy hour until 8 ask your host about specials", (await BuildAsync()).Message);
    }

    /// <summary>A modifier, not a look: it composes with whatever else the venue chose.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_CarriesWhetherTheLabelIsPinned()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueePinLabel = true });

        Assert.True((await BuildAsync()).PinLabel);
    }

    [Fact]
    public async Task BuildMarqueeAsync_LabelNotPinned_SaysSo()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true });

        Assert.False((await BuildAsync()).PinLabel);
    }

    [Fact]
    public async Task BuildMarqueeAsync_NoScrollSpeedChosen_LeavesItToTheScreen()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true });

        Assert.Equal(0, (await BuildAsync()).ScrollSpeed);
    }

    [Fact]
    public async Task BuildMarqueeAsync_CarriesTheVenuesScrollSpeed()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeScrollSpeed = 140 });

        Assert.Equal(140, (await BuildAsync()).ScrollSpeed);
    }

    /// <summary>Zero is a message-only band, not a broken one; the venue asked for no names.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_ZeroSingerCount_KeepsTheMessageAndNamesNobody()
    {
        Arrange(
            new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 0, MarqueeMessage = "Happy hour" },
            Singer("Ada"));

        var command = await BuildAsync();

        Assert.True(command.Enabled);
        Assert.Empty(Turns(command));
        Assert.Equal("Happy hour", command.Message);
    }

    /// <summary>A count past the queue's length is a quiet night, not an exception.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_MoreSingersWantedThanQueued_TakesWhatThereIs()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 5 }, Singer("Ada"));

        Assert.Equal(["Ada"], Turns(await BuildAsync()));
    }

    /// <summary>Zero is "the screen decides", and the screen is what holds that default.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_NoFontSizeChosen_LeavesItToTheScreen()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true });

        Assert.Equal(0, (await BuildAsync()).FontSizePixels);
    }

    [Fact]
    public async Task BuildMarqueeAsync_CarriesTheVenuesFontSize()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeFontSizePixels = 44 });

        Assert.Equal(44, (await BuildAsync()).FontSizePixels);
    }

    [Fact]
    public async Task BuildMarqueeAsync_CarriesPositionAndColours()
    {
        Arrange(new Venue.VenueSettings
        {
            MarqueeEnabled = true,
            MarqueePosition = MarqueePosition.Top,
            MarqueeBackgroundColor = "#101820",
            MarqueeTextColor = "#f2f2f5",
        });

        var command = await BuildAsync();

        Assert.Equal(MarqueePosition.Top, command.Position);
        Assert.Equal("#101820", command.BackgroundColor);
        Assert.Equal("#f2f2f5", command.TextColor);
    }

    /// <summary>A cleared colour must not hand the screen an empty CSS value instead of a default.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BuildMarqueeAsync_BlankColoursAndMessage_ArriveAsNull(string blank)
    {
        Arrange(new Venue.VenueSettings
        {
            MarqueeEnabled = true,
            MarqueeMessage = blank,
            MarqueeBackgroundColor = blank,
            MarqueeTextColor = blank,
        });

        var command = await BuildAsync();

        Assert.Null(command.Message);
        Assert.Null(command.BackgroundColor);
        Assert.Null(command.TextColor);
    }

    /// <summary>"Up next" over who the room is already watching reads as the band a song behind.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_SomeoneIsSinging_LeavesThemOutOfUpNext()
    {
        var ada = Singer("Ada");
        var grace = Singer("Grace");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 3 }, ada, grace);
        Singing(ada);

        Assert.Equal(["Grace"], Turns(await BuildAsync()));
    }

    /// <summary>Dropping the singer must not cost the venue a slot on the band.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_SomeoneIsSinging_StillFillsTheVenuesSingerCount()
    {
        var ada = Singer("Ada");
        Arrange(
            new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 2 },
            ada, Singer("Grace"), Singer("Linus"));
        Singing(ada);

        Assert.Equal(["Grace", "Linus"], Turns(await BuildAsync()));
    }

    /// <summary>Nothing playing is the ordinary case: the whole queue is up next.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_NothingPlaying_NamesEveryQueuedSinger()
    {
        Arrange(
            new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 2 },
            Singer("Ada"), Singer("Grace"));

        Assert.Equal(["Ada", "Grace"], Turns(await BuildAsync()));
    }

    /// <summary>A one-singer room has nothing up next; the screen hides, not a bare label.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_TheOnlySingerIsSinging_NamesNobody()
    {
        var ada = Singer("Ada");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 3 }, ada);
        Singing(ada);

        Assert.Empty(Turns(await BuildAsync()));
    }

    /// <summary>Rejoins each turn's segments back into the string the old flat <c>Singers</c> list
    /// carried, splitting on the <see cref="MarqueeSegmentKind.Separator"/> the provider now sends
    /// between turns instead of the screen inserting one itself.</summary>
    private static IReadOnlyList<string> Turns(SetMarqueeCommand command)
    {
        if (command.Entries.Count == 0) return [];

        var turns = new List<string>();
        var current = new List<string>();

        foreach (var segment in command.Entries)
        {
            if (segment.Kind == MarqueeSegmentKind.Separator)
            {
                turns.Add(string.Concat(current));
                current.Clear();
            }
            else
            {
                current.Add(segment.Text);
            }
        }

        turns.Add(string.Concat(current));
        return turns;
    }

    private void Singing(KHostUser singer)
        => _playback.CurrentPerformance.Returns(new Performance { SingerId = singer.Id, MediaId = Guid.NewGuid() });

    private void Arrange(Venue.VenueSettings settings, params KHostUser[] queued)
    {
        _venues.ReadSelectedVenueAsync().Returns(new Venue { Name = "The Bar", Settings = settings });
        _queue.Users.Returns(queued);
    }

    private static KHostUser Singer(string name) => new() { Id = Guid.NewGuid(), Name = name };

    private void Queued(KHostUser singer, string title, string artist = "", string? sungAs = null)
    {
        var mediaId = Guid.NewGuid();
        var queued = _performances.ReadQueuedAsync().Result;

        queued.Add(new Performance { SingerId = singer.Id, MediaId = mediaId, SungAs = sungAs });
        _performances.ReadQueuedAsync().Returns(queued);
        _media.ReadAsync(mediaId).Returns(new Media { Id = mediaId, Title = title, Artist = artist, FilePath = "/x.mp4" });
    }

    [Fact]
    public async Task UpNext_ANameQueuedWithTheSong_IsTheOneTheBandSays()
    {
        var singer = Singer("Priya");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1, AllowAliases = true }, singer);
        Queued(singer, "Africa", sungAs: "DJ P");

        var command = await BuildAsync();

        // The room is watching someone who typed their own name into a phone; the band saying the
        // account name would be naming a person nobody in the room is looking for.
        Assert.Equal("Africa - DJ P", Assert.Single(Turns(command)));
    }

    [Fact]
    public async Task UpNext_TheVenueRefusesAliases_SaysTheSingerItKnows()
    {
        var singer = Singer("Priya");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1, AllowAliases = false }, singer);
        Queued(singer, "Africa", sungAs: "DJ P");

        var command = await BuildAsync();

        Assert.Equal("Africa - Priya", Assert.Single(Turns(command)));
    }

    [Fact]
    public async Task UpNext_NoNameQueuedWithTheSong_StillNamesTheSinger()
    {
        var singer = Singer("Priya");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1, AllowAliases = true }, singer);
        Queued(singer, "Africa");

        var command = await BuildAsync();

        Assert.Equal("Africa - Priya", Assert.Single(Turns(command)));
    }

    // --- structured segments, so a display can colour a singer's name apart from a song's title ---

    /// <summary>The default format's song then singer, each in its own kind, with the literal
    /// " - " between them kept as plain text so it takes the band's own colour, not either one's.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_SingerHasASong_SegmentsSongThenSinger()
    {
        var ada = Singer("Ada");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1 }, ada);
        Queued(ada, "Bohemian Rhapsody");

        var entries = (await BuildAsync()).Entries;

        Assert.Equal(
            [(MarqueeSegmentKind.Song, "Bohemian Rhapsody"), (MarqueeSegmentKind.Other, " - "), (MarqueeSegmentKind.Singer, "Ada")],
            entries.Select(s => (s.Kind, s.Text)));
    }

    /// <summary>Nothing queued is one plain singer segment — there is no song run to colour.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_SingerHasNoSong_SegmentsSingerAlone()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1 }, Singer("Ada"));

        var entries = (await BuildAsync()).Entries;

        Assert.Equal([(MarqueeSegmentKind.Singer, "Ada")], entries.Select(s => (s.Kind, s.Text)));
    }

    /// <summary>Two turns carry one separator between them, and none at either end.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_SeveralSingers_OneSeparatorBetweenTurns()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 2 }, Singer("Ada"), Singer("Grace"));

        var entries = (await BuildAsync()).Entries;

        Assert.Equal(
            [MarqueeSegmentKind.Singer, MarqueeSegmentKind.Separator, MarqueeSegmentKind.Singer],
            entries.Select(s => s.Kind));
    }

    /// <summary>The venue's own line is "other" wording; it never becomes a segment array of its
    /// own, so it keeps the band's plain colour regardless of the singer/song choices.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_MessageOnly_CarriesNoEntrySegments()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 0, MarqueeMessage = "Happy hour" });

        var command = await BuildAsync();

        Assert.Empty(command.Entries);
        Assert.Equal("Happy hour", command.Message);
    }

    [Fact]
    public async Task BuildMarqueeAsync_NoColoursChosen_LeaveSingerSongAndDividerColourNull()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true });

        var command = await BuildAsync();

        Assert.Null(command.SingerColor);
        Assert.Null(command.SongColor);
        Assert.Null(command.DividerColor);
    }

    [Fact]
    public async Task BuildMarqueeAsync_CarriesTheVenuesSingerSongAndDividerColours()
    {
        Arrange(new Venue.VenueSettings
        {
            MarqueeEnabled = true,
            MarqueeSingerColor = "#ff8800",
            MarqueeSongColor = "#00ffaa",
            MarqueeDividerColor = "#8888ff",
        });

        var command = await BuildAsync();

        Assert.Equal("#ff8800", command.SingerColor);
        Assert.Equal("#00ffaa", command.SongColor);
        Assert.Equal("#8888ff", command.DividerColor);
    }

    [Fact]
    public async Task BuildMarqueeAsync_NoOpacityChosen_LeavesItToTheScreen()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true });

        Assert.Null((await BuildAsync()).BackgroundOpacityPercent);
    }

    /// <summary>Zero is a real, fully-transparent choice, not "unset" the way the pixel settings use it.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_OpacitySetToZero_IsCarriedAsZeroNotNull()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeBackgroundOpacity = 0 });

        Assert.Equal(0, (await BuildAsync()).BackgroundOpacityPercent);
    }

    [Fact]
    public async Task BuildMarqueeAsync_CarriesTheVenuesOpacity()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeBackgroundOpacity = 40 });

        Assert.Equal(40, (await BuildAsync()).BackgroundOpacityPercent);
    }

    /// <summary>Dot is the zero value: an old row with no divider shape reads as today's look.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_NoDividerShapeChosen_ResolvesToTheDot()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 2 }, Singer("Ada"), Singer("Grace"));

        Assert.Equal("•", (await BuildAsync()).DividerGlyph);
    }

    [Theory]
    [InlineData(MarqueeDividerShape.Dot, "•")]
    [InlineData(MarqueeDividerShape.Diamond, "◆")]
    [InlineData(MarqueeDividerShape.Star, "★")]
    [InlineData(MarqueeDividerShape.Slash, "/")]
    [InlineData(MarqueeDividerShape.Pipe, "|")]
    [InlineData(MarqueeDividerShape.Note, "♪")]
    public async Task BuildMarqueeAsync_EachDividerShape_ResolvesItsOwnGlyph(MarqueeDividerShape shape, string glyph)
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 2, MarqueeDividerShape = shape }, Singer("Ada"), Singer("Grace"));

        Assert.Equal(glyph, (await BuildAsync()).DividerGlyph);
    }

    /// <summary>"None" removes the divider entirely — no glyph, and no separator segment either,
    /// since a screen given neither has nothing to draw one with.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_DividerShapeNone_SendsNoGlyphAndNoSeparatorSegments()
    {
        Arrange(
            new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 2, MarqueeDividerShape = MarqueeDividerShape.None },
            Singer("Ada"), Singer("Grace"));

        var command = await BuildAsync();

        Assert.Null(command.DividerGlyph);
        Assert.DoesNotContain(MarqueeSegmentKind.Separator, command.Entries.Select(s => s.Kind));
    }
}
