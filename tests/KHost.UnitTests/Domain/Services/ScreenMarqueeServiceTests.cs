using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

public class ScreenMarqueeServiceTests
{
    private readonly IScreenServer _screens = Substitute.For<IScreenServer>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    private ScreenMarqueeService Service() => new(
        NullLogger<ScreenMarqueeService>.Instance, _screens, _venues, _queue, _performances, _media, _playback, _broker);

    public ScreenMarqueeServiceTests()
        // NSubstitute hands back a task wrapping null otherwise, and the composition .Where()s it.
        => _performances.ReadQueuedAsync().Returns([]);

    [Fact]
    public async Task BuildAsync_NoVenueSelected_IsDisabled()
    {
        _venues.ReadSelectedVenueAsync().Returns((Venue?)null);

        Assert.False((await Service().BuildAsync()).Enabled);
    }

    [Fact]
    public async Task BuildAsync_VenueHasMarqueeOff_IsDisabled()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = false, MarqueeMessage = "Ignored" });

        Assert.False((await Service().BuildAsync()).Enabled);
    }

    [Fact]
    public async Task BuildAsync_MarqueeOn_TakesOnlyTheVenuesSingerCountInQueueOrder()
    {
        Arrange(
            new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 2 },
            Singer("Ada"), Singer("Grace"), Singer("Linus"));

        var command = await Service().BuildAsync();

        Assert.Equal(["Ada", "Grace"], command.Singers);
    }

    /// <summary>The room is looking for the song as much as the name, so the band leads with it.</summary>
    [Fact]
    public async Task BuildAsync_SingerHasASongQueued_ReadsSongThenSinger()
    {
        var ada = Singer("Ada");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1 }, ada);
        Queued(ada, "Bohemian Rhapsody");

        Assert.Equal(["Bohemian Rhapsody - Ada"], (await Service().BuildAsync()).Singers);
    }

    /// <summary>A host's own wording replaces "{song} - {singer}", tag for tag.</summary>
    [Fact]
    public async Task BuildAsync_CustomEntryFormat_UsesTheVenuesWording()
    {
        var ada = Singer("Ada");
        Arrange(
            new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1, MarqueeEntryFormat = "{artist} - {song}" },
            ada);
        Queued(ada, "Bohemian Rhapsody", "Queen");

        Assert.Equal(["Queen - Bohemian Rhapsody"], (await Service().BuildAsync()).Singers);
    }

    /// <summary>Numbering starts at one, matching how a host would read the list aloud.</summary>
    [Fact]
    public async Task BuildAsync_EntryFormatUsesPosition_NumbersFromOne()
    {
        var ada = Singer("Ada");
        var grace = Singer("Grace");
        Arrange(
            new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 2, MarqueeEntryFormat = "{position}. {song} - {singer}" },
            ada, grace);
        Queued(ada, "Africa");
        Queued(grace, "Wonderwall");

        Assert.Equal(["1. Africa - Ada", "2. Wonderwall - Grace"], (await Service().BuildAsync()).Singers);
    }

    /// <summary>Tags read the same regardless of how a host capitalises them while typing.</summary>
    [Fact]
    public async Task BuildAsync_EntryFormatTagsAreCaseInsensitive()
    {
        var ada = Singer("Ada");
        Arrange(
            new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1, MarqueeEntryFormat = "{SONG} by {Singer}" },
            ada);
        Queued(ada, "Africa");

        Assert.Equal(["Africa by Ada"], (await Service().BuildAsync()).Singers);
    }

    /// <summary>A blank format would compose empty lines, so it reads as unset.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BuildAsync_BlankEntryFormat_FallsBackToTheDefault(string blank)
    {
        var ada = Singer("Ada");
        Arrange(
            new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1, MarqueeEntryFormat = blank },
            ada);
        Queued(ada, "Bohemian Rhapsody");

        Assert.Equal(["Bohemian Rhapsody - Ada"], (await Service().BuildAsync()).Singers);
    }

    /// <summary>A singer with nothing queued must still show, or the band disagrees with the queue.</summary>
    [Fact]
    public async Task BuildAsync_SingerHasNoSongQueued_NamesThemAlone()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1 }, Singer("Ada"));

        Assert.Equal(["Ada"], (await Service().BuildAsync()).Singers);
    }

    /// <summary>Each singer gets their own song, not the first one in the queue.</summary>
    [Fact]
    public async Task BuildAsync_SeveralSingers_PairsEachWithTheirOwnSong()
    {
        var ada = Singer("Ada");
        var grace = Singer("Grace");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 2 }, ada, grace);
        Queued(ada, "Africa");
        Queued(grace, "Wonderwall");

        Assert.Equal(["Africa - Ada", "Wonderwall - Grace"], (await Service().BuildAsync()).Singers);
    }

    /// <summary>A media row that has lost its title must not read as " - Ada".</summary>
    [Fact]
    public async Task BuildAsync_QueuedSongHasNoTitle_NamesTheSingerAlone()
    {
        var ada = Singer("Ada");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1 }, ada);
        Queued(ada, "   ");

        Assert.Equal(["Ada"], (await Service().BuildAsync()).Singers);
    }

    /// <summary>The band is one line; a pasted message keeps its words and loses its shape.</summary>
    [Fact]
    public async Task BuildAsync_MessageSpansLines_ArrivesAsOneLine()
    {
        Arrange(new Venue.VenueSettings
        {
            MarqueeEnabled = true,
            MarqueeMessage = "Happy hour until 8\n\nask your host   about specials",
        });

        Assert.Equal("Happy hour until 8 ask your host about specials", (await Service().BuildAsync()).Message);
    }

    /// <summary>A modifier, not a look: it composes with whatever else the venue chose.</summary>
    [Fact]
    public async Task BuildAsync_CarriesWhetherTheLabelIsPinned()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueePinLabel = true });

        Assert.True((await Service().BuildAsync()).PinLabel);
    }

    [Fact]
    public async Task BuildAsync_LabelNotPinned_SaysSo()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true });

        Assert.False((await Service().BuildAsync()).PinLabel);
    }

    [Fact]
    public async Task BuildAsync_NoScrollSpeedChosen_LeavesItToTheScreen()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true });

        Assert.Equal(0, (await Service().BuildAsync()).ScrollSpeed);
    }

    [Fact]
    public async Task BuildAsync_CarriesTheVenuesScrollSpeed()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeScrollSpeed = 140 });

        Assert.Equal(140, (await Service().BuildAsync()).ScrollSpeed);
    }

    /// <summary>A song enqueued changes what the band says, not just who is on it.</summary>
    [Fact]
    public async Task PerformancesChanged_ResendsTheMarquee()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true });

        using var service = Service();

        _broker.Announce(new PerformancesChanged());

        await WaitForBroadcastAsync();
    }

    /// <summary>Zero is a message-only band, not a broken one; the venue asked for no names.</summary>
    [Fact]
    public async Task BuildAsync_ZeroSingerCount_KeepsTheMessageAndNamesNobody()
    {
        Arrange(
            new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 0, MarqueeMessage = "Happy hour" },
            Singer("Ada"));

        var command = await Service().BuildAsync();

        Assert.True(command.Enabled);
        Assert.Empty(command.Singers);
        Assert.Equal("Happy hour", command.Message);
    }

    /// <summary>A count past the queue's length is a quiet night, not an exception.</summary>
    [Fact]
    public async Task BuildAsync_MoreSingersWantedThanQueued_TakesWhatThereIs()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 5 }, Singer("Ada"));

        Assert.Equal(["Ada"], (await Service().BuildAsync()).Singers);
    }

    /// <summary>Zero is "the screen decides", and the screen is what holds that default.</summary>
    [Fact]
    public async Task BuildAsync_NoFontSizeChosen_LeavesItToTheScreen()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true });

        Assert.Equal(0, (await Service().BuildAsync()).FontSizePixels);
    }

    [Fact]
    public async Task BuildAsync_CarriesTheVenuesFontSize()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeFontSizePixels = 44 });

        Assert.Equal(44, (await Service().BuildAsync()).FontSizePixels);
    }

    [Fact]
    public async Task BuildAsync_CarriesPositionAndColours()
    {
        Arrange(new Venue.VenueSettings
        {
            MarqueeEnabled = true,
            MarqueePosition = MarqueePosition.Top,
            MarqueeBackgroundColor = "#101820",
            MarqueeTextColor = "#f2f2f5",
        });

        var command = await Service().BuildAsync();

        Assert.Equal(MarqueePosition.Top, command.Position);
        Assert.Equal("#101820", command.BackgroundColor);
        Assert.Equal("#f2f2f5", command.TextColor);
    }

    /// <summary>A cleared colour must not hand the screen an empty CSS value instead of a default.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BuildAsync_BlankColoursAndMessage_ArriveAsNull(string blank)
    {
        Arrange(new Venue.VenueSettings
        {
            MarqueeEnabled = true,
            MarqueeMessage = blank,
            MarqueeBackgroundColor = blank,
            MarqueeTextColor = blank,
        });

        var command = await Service().BuildAsync();

        Assert.Null(command.Message);
        Assert.Null(command.BackgroundColor);
        Assert.Null(command.TextColor);
    }

    [Fact]
    public async Task InitializeAsync_SendsTheMarqueeToEveryScreen()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1 }, Singer("Ada"));

        await Service().InitializeAsync();

        await _screens.Received(1).BroadcastCommandAsync(
            Arg.Is<SetMarqueeCommand>(c => c.Enabled && c.Singers.Count == 1));
    }

    /// <summary>A dequeue or reorder must reach the room without waiting for a venue edit.</summary>
    [Fact]
    public async Task SingerQueueChanged_ResendsTheMarquee()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1 }, Singer("Ada"));

        using var service = Service();

        _broker.Announce(new SingerQueueChanged());

        await WaitForBroadcastAsync();
    }

    /// <summary>Editing the venue turns the marquee on; it cannot wait for a queue move.</summary>
    [Fact]
    public async Task SelectedVenueChanged_ResendsTheMarquee()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true });

        using var service = Service();

        _broker.Announce(new SelectedVenueChanged());

        await WaitForBroadcastAsync();
    }

    /// <summary>A screen joining mid-show has never been sent one, and the room would see nothing.</summary>
    [Fact]
    public async Task ScreenConnected_SendsTheMarqueeToThatScreenAlone()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true });

        var connection = Substitute.For<IScreenConnection>();
        connection.ScreenId.Returns("screen-2");

        using var service = Service();

        _screens.ScreenConnected += Raise.EventWith(new ScreenConnectionEventArgs { Connection = connection });

        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (_screens.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IScreenServer.SendCommandAsync)))
                break;

            await Task.Delay(10);
        }

        await _screens.Received(1).SendCommandAsync("screen-2", Arg.Any<SetMarqueeCommand>());
        await _screens.DidNotReceive().BroadcastCommandAsync(Arg.Any<SetMarqueeCommand>());
    }

    /// <summary>Disposing must release the broker, or a rebuilt service leaves the old publishing.</summary>
    [Fact]
    public async Task Dispose_StopsRespondingToTheQueue()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true });

        var service = Service();
        service.Dispose();

        _broker.Announce(new SingerQueueChanged());
        await Task.Delay(50);

        await _screens.DidNotReceive().BroadcastCommandAsync(Arg.Any<SetMarqueeCommand>());
    }

    /// <summary>"Up next" over who the room is already watching reads as the band a song behind.</summary>
    [Fact]
    public async Task BuildAsync_SomeoneIsSinging_LeavesThemOutOfUpNext()
    {
        var ada = Singer("Ada");
        var grace = Singer("Grace");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 3 }, ada, grace);
        Singing(ada);

        Assert.Equal(["Grace"], (await Service().BuildAsync()).Singers);
    }

    /// <summary>Dropping the singer must not cost the venue a slot on the band.</summary>
    [Fact]
    public async Task BuildAsync_SomeoneIsSinging_StillFillsTheVenuesSingerCount()
    {
        var ada = Singer("Ada");
        Arrange(
            new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 2 },
            ada, Singer("Grace"), Singer("Linus"));
        Singing(ada);

        Assert.Equal(["Grace", "Linus"], (await Service().BuildAsync()).Singers);
    }

    /// <summary>Nothing playing is the ordinary case: the whole queue is up next.</summary>
    [Fact]
    public async Task BuildAsync_NothingPlaying_NamesEveryQueuedSinger()
    {
        Arrange(
            new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 2 },
            Singer("Ada"), Singer("Grace"));

        Assert.Equal(["Ada", "Grace"], (await Service().BuildAsync()).Singers);
    }

    /// <summary>A one-singer room has nothing up next; the screen hides, not a bare label.</summary>
    [Fact]
    public async Task BuildAsync_TheOnlySingerIsSinging_NamesNobody()
    {
        var ada = Singer("Ada");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 3 }, ada);
        Singing(ada);

        Assert.Empty((await Service().BuildAsync()).Singers);
    }

    /// <summary>A song starting changes who is up next, so the screens have to hear about it.</summary>
    [Fact]
    public async Task PlaybackChanged_RepublishesTheMarquee()
    {
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1 }, Singer("Ada"));
        using var service = Service();

        _broker.Announce(new PlaybackChanged());

        await WaitForBroadcastAsync();
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

    // The handlers hand off to Task.Run so the hub thread is never held, so an assertion made
    // straight after an announce races the publish rather than observing it.
    private async Task WaitForBroadcastAsync()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (_screens.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IScreenServer.BroadcastCommandAsync)))
                return;

            await Task.Delay(10);
        }

        Assert.Fail("The marquee was never broadcast.");
    }
    [Fact]
    public async Task UpNext_ANameQueuedWithTheSong_IsTheOneTheBandSays()
    {
        var singer = Singer("Priya");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1, AllowAliases = true }, singer);
        Queued(singer, "Africa", sungAs: "DJ P");

        var command = await Service().BuildAsync();

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

        var command = await Service().BuildAsync();

        Assert.Equal("Africa - Priya", Assert.Single(command.Singers));
    }

    [Fact]
    public async Task UpNext_NoNameQueuedWithTheSong_StillNamesTheSinger()
    {
        var singer = Singer("Priya");
        Arrange(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 1, AllowAliases = true }, singer);
        Queued(singer, "Africa");

        var command = await Service().BuildAsync();

        Assert.Equal("Africa - Priya", Assert.Single(command.Singers));
    }

}
