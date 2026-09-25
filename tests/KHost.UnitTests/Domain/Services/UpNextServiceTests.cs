using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

public class UpNextServiceTests : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(100);

    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly List<UpNextService> _built = [];
    private readonly List<IDisposable> _subscriptions = [];

    public UpNextServiceTests()
    {
        // NSubstitute hands back a task wrapping null otherwise, and the list .FirstOrDefault()s it.
        _performances.ReadQueuedAsync().Returns([]);
        _queue.Users.Returns([]);
        ArrangeVenue(aliases: false);
    }

    public void Dispose()
    {
        foreach (var service in _built)
            service.Dispose();

        foreach (var subscription in _subscriptions)
            subscription.Dispose();
    }

    // --- the list ---

    [Fact]
    public async Task ReadAsync_ListsSingersInQueueOrder_EachWithTheirFirstQueuedSong_NumberedFromOne()
    {
        var ada = Singer("Ada");
        var bo = Singer("Bo");
        Waiting(ada, bo);
        Queued(ada, "Africa", "Toto");
        Queued(ada, "Rosanna", "Toto");
        Queued(bo, "Jolene", "Dolly Parton");

        var entries = await Service().ReadAsync(5);

        Assert.Equal(
            [
                new UpNextEntry { Position = 1, Singer = "Ada", Title = "Africa", Artist = "Toto" },
                new UpNextEntry { Position = 2, Singer = "Bo", Title = "Jolene", Artist = "Dolly Parton" },
            ],
            entries);
    }

    [Fact]
    public async Task ReadAsync_TakesOnlyTheCountAskedFor()
    {
        Waiting(Singer("Ada"), Singer("Bo"), Singer("Cy"));

        var entries = await Service().ReadAsync(2);

        Assert.Equal(["Ada", "Bo"], entries.Select(entry => entry.Singer));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ReadAsync_NoCount_IsEmpty(int count)
    {
        Waiting(Singer("Ada"));

        Assert.Empty(await Service().ReadAsync(count));
    }

    /// <summary>Left out before the count, so asking for two still names two who have yet to sing.</summary>
    [Fact]
    public async Task ReadAsync_TheSingerAtTheMic_IsLeftOutBeforeCounting()
    {
        var ada = Singer("Ada");
        Waiting(ada, Singer("Bo"), Singer("Cy"));
        _playback.CurrentPerformance.Returns(new Performance { SingerId = ada.Id });

        var entries = await Service().ReadAsync(2);

        Assert.Equal([(1, "Bo"), (2, "Cy")], entries.Select(entry => (entry.Position, entry.Singer)));
    }

    [Fact]
    public async Task ReadAsync_ASingerWithNothingQueued_IsListedWithNoSong()
    {
        Waiting(Singer("Ada"));

        var entry = Assert.Single(await Service().ReadAsync(3));

        Assert.Equal("Ada", entry.Singer);
        Assert.Null(entry.Title);
        Assert.Null(entry.Artist);
    }

    /// <summary>A song with no artist recorded says so with null, never a blank to reason about.</summary>
    [Fact]
    public async Task ReadAsync_ASongWithNoArtist_HasANullArtist()
    {
        var ada = Singer("Ada");
        Waiting(ada);
        Queued(ada, "  Africa  ", "   ");

        var entry = Assert.Single(await Service().ReadAsync(1));

        Assert.Equal("Africa", entry.Title);
        Assert.Null(entry.Artist);
    }

    [Fact]
    public async Task ReadAsync_TheVenueAllowsAliases_NamesTheSingerAsTheyAsked()
    {
        ArrangeVenue(aliases: true);
        var ada = Singer("Ada");
        Waiting(ada);
        Queued(ada, "Africa", sungAs: " DJ A ");

        Assert.Equal("DJ A", Assert.Single(await Service().ReadAsync(1)).Singer);
    }

    [Fact]
    public async Task ReadAsync_TheVenueRefusesAliases_NamesTheAccount()
    {
        var ada = Singer("Ada");
        Waiting(ada);
        Queued(ada, "Africa", sungAs: "DJ A");

        Assert.Equal("Ada", Assert.Single(await Service().ReadAsync(1)).Singer);
    }

    // --- UpNextChanged ---

    /// <summary>A stop is the playback, the dequeue and the rotation; the list moved once.</summary>
    [Fact]
    public async Task OneActionsSeveralAnnouncements_AnnounceUpNextChangedOnce()
    {
        var counted = Count();
        Service();
        _playback.CurrentPerformance.Returns(new Performance { SingerId = Guid.NewGuid() });

        _broker.Announce(new PlaybackChanged());
        _broker.Announce(new PerformancesChanged());
        _broker.Announce(new SingerQueueChanged());

        Assert.True(await WaitUntilAsync(() => counted() > 0));
        await Task.Delay(Settle * 3);

        Assert.Equal(1, counted());
    }

    [Fact]
    public async Task TwoActionsApart_AnnounceTwice()
    {
        var counted = Count();
        Service();

        _broker.Announce(new SingerQueueChanged());
        Assert.True(await WaitUntilAsync(() => counted() == 1));
        await Task.Delay(Settle * 2);

        _broker.Announce(new PerformancesChanged());
        Assert.True(await WaitUntilAsync(() => counted() == 2));
        await Task.Delay(Settle * 3);

        Assert.Equal(2, counted());
    }

    /// <summary>A pause or a seek is PlaybackChanged too, and moves nobody.</summary>
    [Fact]
    public async Task PlaybackChanged_TheSameSingerAtTheMic_AnnouncesNothing()
    {
        var counted = Count();
        Service();

        _broker.Announce(new PlaybackChanged());
        await Task.Delay(Settle * 3);

        Assert.Equal(0, counted());
    }

    [Fact]
    public async Task PlaybackChanged_ADifferentSingerAtTheMic_Announces()
    {
        var counted = Count();
        Service();
        _playback.CurrentPerformance.Returns(new Performance { SingerId = Guid.NewGuid() });

        _broker.Announce(new PlaybackChanged());

        Assert.True(await WaitUntilAsync(() => counted() == 1));
    }

    /// <summary>Most venue edits are colours and wording; only the alias rule renames anybody.</summary>
    [Fact]
    public async Task SelectedVenueChanged_OnlyTheAliasRuleMovesTheList()
    {
        var counted = Count();
        Service();

        // The first one is news: nothing was known about the rule before it.
        _broker.Announce(new SelectedVenueChanged());
        Assert.True(await WaitUntilAsync(() => counted() == 1));
        await Task.Delay(Settle * 2);

        _broker.Announce(new SelectedVenueChanged());
        await Task.Delay(Settle * 3);
        Assert.Equal(1, counted());

        ArrangeVenue(aliases: true);
        _broker.Announce(new SelectedVenueChanged());
        Assert.True(await WaitUntilAsync(() => counted() == 2));
    }

    [Fact]
    public async Task Dispose_StopsAnnouncing()
    {
        var counted = Count();
        Service().Dispose();

        _broker.Announce(new SingerQueueChanged());
        await Task.Delay(Settle * 3);

        Assert.Equal(0, counted());
    }

    // Built after every arrangement: it subscribes in its constructor.
    private UpNextService Service()
    {
        var service = new UpNextService(
            NullLogger<UpNextService>.Instance, _broker, _venues, _queue, _performances, _media,
            new ServiceCollection().AddSingleton(_playback).BuildServiceProvider(), Settle);
        _built.Add(service);

        return service;
    }

    private Func<int> Count()
    {
        var raised = 0;
        _subscriptions.Add(_broker.Subscribe<UpNextChanged>(_ => Interlocked.Increment(ref raised)));

        return () => Volatile.Read(ref raised);
    }

    private void ArrangeVenue(bool aliases)
        => _venues.ReadSelectedVenueAsync().Returns(new Venue { Name = "The Bar", Settings = new Venue.VenueSettings { AllowAliases = aliases } });

    private static KHostUser Singer(string name) => new() { Id = Guid.NewGuid(), Name = name };

    private void Waiting(params KHostUser[] singers) => _queue.Users.Returns(singers);

    private void Queued(KHostUser singer, string title, string artist = "", string? sungAs = null)
    {
        var mediaId = Guid.NewGuid();
        var queued = _performances.ReadQueuedAsync().Result;

        queued.Add(new Performance { SingerId = singer.Id, MediaId = mediaId, SungAs = sungAs });
        _performances.ReadQueuedAsync().Returns(queued);
        _media.ReadAsync(mediaId).Returns(new Media { Id = mediaId, Title = title, Artist = artist, FilePath = "/x.mp4" });
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }

        return false;
    }
}
