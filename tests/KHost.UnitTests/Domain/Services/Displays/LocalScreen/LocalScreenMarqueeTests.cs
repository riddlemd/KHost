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

        Assert.Equal(["Ada", "Grace"], command.Singers);
    }

    /// <summary>The room is looking for the song as much as the name, so the band leads with it.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_SingerHasASongQueued_ReadsSongThenSinger()
    {
        var ada = Singer("Ada");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1 }, ada);
        Queued(ada, "Bohemian Rhapsody");

        Assert.Equal(["Bohemian Rhapsody - Ada"], (await BuildAsync()).Singers);
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

        Assert.Equal(["Queen - Bohemian Rhapsody"], (await BuildAsync()).Singers);
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

        Assert.Equal(["1. Africa - Ada", "2. Wonderwall - Grace"], (await BuildAsync()).Singers);
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

        Assert.Equal(["Africa by Ada"], (await BuildAsync()).Singers);
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

        Assert.Equal(["Bohemian Rhapsody - Ada"], (await BuildAsync()).Singers);
    }

    /// <summary>A singer with nothing queued must still show, or the band disagrees with the queue.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_SingerHasNoSongQueued_NamesThemAlone()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1 }, Singer("Ada"));

        Assert.Equal(["Ada"], (await BuildAsync()).Singers);
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

        Assert.Equal(["Africa - Ada", "Wonderwall - Grace"], (await BuildAsync()).Singers);
    }

    /// <summary>A media row that has lost its title must not read as " - Ada".</summary>
    [Fact]
    public async Task BuildMarqueeAsync_QueuedSongHasNoTitle_NamesTheSingerAlone()
    {
        var ada = Singer("Ada");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1 }, ada);
        Queued(ada, "   ");

        Assert.Equal(["Ada"], (await BuildAsync()).Singers);
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
        Assert.Empty(command.Singers);
        Assert.Equal("Happy hour", command.Message);
    }

    /// <summary>A count past the queue's length is a quiet night, not an exception.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_MoreSingersWantedThanQueued_TakesWhatThereIs()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 5 }, Singer("Ada"));

        Assert.Equal(["Ada"], (await BuildAsync()).Singers);
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

        Assert.Equal(["Grace"], (await BuildAsync()).Singers);
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

        Assert.Equal(["Grace", "Linus"], (await BuildAsync()).Singers);
    }

    /// <summary>Nothing playing is the ordinary case: the whole queue is up next.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_NothingPlaying_NamesEveryQueuedSinger()
    {
        Arrange(
            new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 2 },
            Singer("Ada"), Singer("Grace"));

        Assert.Equal(["Ada", "Grace"], (await BuildAsync()).Singers);
    }

    /// <summary>A one-singer room has nothing up next; the screen hides, not a bare label.</summary>
    [Fact]
    public async Task BuildMarqueeAsync_TheOnlySingerIsSinging_NamesNobody()
    {
        var ada = Singer("Ada");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 3 }, ada);
        Singing(ada);

        Assert.Empty((await BuildAsync()).Singers);
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
        Assert.Equal("Africa - DJ P", Assert.Single(command.Singers));
    }

    [Fact]
    public async Task UpNext_TheVenueRefusesAliases_SaysTheSingerItKnows()
    {
        var singer = Singer("Priya");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1, AllowAliases = false }, singer);
        Queued(singer, "Africa", sungAs: "DJ P");

        var command = await BuildAsync();

        Assert.Equal("Africa - Priya", Assert.Single(command.Singers));
    }

    [Fact]
    public async Task UpNext_NoNameQueuedWithTheSong_StillNamesTheSinger()
    {
        var singer = Singer("Priya");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1, AllowAliases = true }, singer);
        Queued(singer, "Africa");

        var command = await BuildAsync();

        Assert.Equal("Africa - Priya", Assert.Single(command.Singers));
    }

}
