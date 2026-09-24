using KHost.Abstractions.Models;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Domain.Services.Displays;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services.Displays;

/// <summary>Who the card names, and when there is nobody to name. Host triggered, so nothing here
/// is about republishing: the card stands until the next thing is drawn.</summary>
public class NextSingerCardServiceTests
{
    private readonly IMessageBroker _broker = Substitute.For<IMessageBroker>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();

    private readonly KHostUser _ada = new() { Id = Guid.NewGuid(), Name = "Ada" };
    private readonly KHostUser _ben = new() { Id = Guid.NewGuid(), Name = "Ben" };
    private readonly Venue _venue = new() { Id = Guid.NewGuid(), Name = "Bar", Settings = new() };

    public NextSingerCardServiceTests()
    {
        _venues.ReadSelectedVenueAsync().Returns(_ => _venue);
        _queue.Users.Returns(_ => []);
        _performances.ReadQueuedAsync().Returns(_ => []);
    }

    [Fact]
    public async Task AnEmptyQueue_HasNobodyToAnnounce()
    {
        Assert.Null(await Service().BuildAsync());
        Assert.False(await Service().AnnounceAsync());
    }

    [Fact]
    public async Task TheCard_NamesTheSingerAtTheTopOfTheQueue()
    {
        Queue(_ada, _ben);

        Assert.Equal("Ada", (await Service().BuildAsync())!.Singer);
    }

    /// <summary>The singer holding the microphone is not up next, and a card saying so would
    /// disagree with the room. The same rule the marquee applies.</summary>
    [Fact]
    public async Task TheSingerAtTheMicrophone_IsSkipped()
    {
        Queue(_ada, _ben);
        _playback.CurrentPerformance.Returns(new Performance { SingerId = _ada.Id, MediaId = Guid.NewGuid() });

        Assert.Equal("Ben", (await Service().BuildAsync())!.Singer);
    }

    [Fact]
    public async Task TheCard_NamesTheirFirstQueuedSong()
    {
        Queue(_ada);
        Queued(_ada, "Total Eclipse of the Heart", "Bonnie Tyler");

        var card = await Service().BuildAsync();

        Assert.Equal("Total Eclipse of the Heart", card!.Song);
        Assert.Equal("Bonnie Tyler", card.Artist);
    }

    /// <summary>A host adds the person before the song, so a singer with nothing queued is a real
    /// state. The card names them alone rather than promising a song that does not exist.</summary>
    [Fact]
    public async Task ASingerWithNothingQueued_IsNamedWithoutASong()
    {
        Queue(_ada);

        var card = await Service().BuildAsync();

        Assert.Equal("Ada", card!.Singer);
        Assert.Null(card.Song);
    }

    /// <summary>Off the performance, never the account: a song-first remote lets a guest type a
    /// name per pick, and the room should hear the one they signed up under.</summary>
    [Fact]
    public async Task AVenueAllowingAliases_AnnouncesTheNameTheTurnWasQueuedUnder()
    {
        _venue.Settings.AllowAliases = true;
        Queue(_ada);
        Queued(_ada, "Today", "The Smashing Pumpkins", sungAs: "flo");

        Assert.Equal("flo", (await Service().BuildAsync())!.Singer);
    }

    /// <summary>A venue that never asked for aliases hears the singer it knows, even though every
    /// enqueue records a name.</summary>
    [Fact]
    public async Task AVenueWithoutAliases_AnnouncesTheSingersOwnName()
    {
        _venue.Settings.AllowAliases = false;
        Queue(_ada);
        Queued(_ada, "Today", "The Smashing Pumpkins", sungAs: "flo");

        Assert.Equal("Ada", (await Service().BuildAsync())!.Singer);
    }

    [Fact]
    public async Task Announcing_HandsTheCardToTheDisplay()
    {
        Queue(_ada);
        Queued(_ada, "Today", "The Smashing Pumpkins");

        Assert.True(await Service().AnnounceAsync());

        await _broker.Received(1).PublishAsync(
            Arg.Is<NextSingerCardRequested>(request => request.Card.Singer == "Ada" && request.Card.Song == "Today"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Nobody to name is nothing to draw: the display must not be asked for an empty card.</summary>
    [Fact]
    public async Task Announcing_AnEmptyQueue_AsksTheDisplayForNothing()
    {
        Assert.False(await Service().AnnounceAsync());

        await _broker.DidNotReceive().PublishAsync(Arg.Any<NextSingerCardRequested>(), Arg.Any<CancellationToken>());
    }

    private void Queue(params KHostUser[] singers) => _queue.Users.Returns(singers);

    private void Queued(KHostUser singer, string title, string artist, string? sungAs = null)
    {
        var mediaId = Guid.NewGuid();

        _performances.ReadQueuedAsync().Returns(_ =>
            [new Performance { SingerId = singer.Id, MediaId = mediaId, SungAs = sungAs }]);

        _media.ReadAsync(mediaId).Returns(_ => new Media
        {
            Id = mediaId,
            Title = title,
            Artist = artist,
            FilePath = "/x.mp4",
        });
    }

    private NextSingerCardService Service() => new(
        NullLogger<NextSingerCardService>.Instance,
        _broker, _venues, _queue, _performances, _media, _playback);
}
