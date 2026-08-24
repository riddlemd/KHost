using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Ads;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services.Ads;

// One active playlist per venue, so the whole job is "is it due yet". The counters must only move
// on an ad that actually reached the screen, or a refusal silently burns the slot.
public class AdServiceTests : IDisposable
{
    /// <summary>Hand-wound: every-N-minutes is untestable against a clock that only moves forwards.</summary>
    private sealed class StoppedClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 8, 24, 20, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly IMediaPoolService _pools = Substitute.For<IMediaPoolService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly StoppedClock _clock = new();
    private bool _adPlays = true;
    private readonly AdService _service;

    private readonly Guid _poolId = Guid.NewGuid();
    private readonly Guid _venueId = Guid.NewGuid();

    public AdServiceTests()
    {
        // A flag rather than re-stubbing mid-test: NSubstitute treats a second Returns on a call
        // that has already run as a new invocation.
        _playback.PlayAdAsync(Arg.Any<Media>()).Returns(_ => Task.FromResult(_adPlays));
        _queue.Users.Returns([]);

        _service = new AdService(
            NullLogger<AdService>.Instance, _playback, _pools, _media, _venues, _queue, _clock);
    }

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    private MediaPool ConfigureVenue(AdTriggerMode trigger, int interval = 3, bool withPool = true)
    {
        var pool = new MediaPool { Id = _poolId, Name = "Spots", Kind = MediaKind.Ad, AdTrigger = trigger, AdTriggerInterval = interval };

        _venues.ReadSelectedVenueAsync().Returns(Task.FromResult<Venue?>(new Venue
        {
            Id = _venueId,
            Name = "The Bar",
            Settings = new Venue.VenueSettings { AdPoolId = withPool ? _poolId : null },
        }));

        _pools.ReadWithEntriesAsync(_poolId).Returns(Task.FromResult<MediaPool?>(pool));

        var media = new Media
        {
            Id = Guid.NewGuid(),
            FilePath = "/media/spot.mp4",
            Title = "Happy Hour",
            Status = MediaStatus.Ready,
            Kind = MediaKind.Ad,
            Duration = TimeSpan.FromSeconds(20),
        };

        _pools.SelectNextAsync(_poolId, Arg.Any<Guid?>()).Returns(Task.FromResult<Guid?>(media.Id));
        _media.ReadAsync(media.Id).Returns(Task.FromResult<Media?>(media));

        return pool;
    }

    private async Task PerformancesAsync(int count)
    {
        for (var i = 0; i < count; i++)
            await _service.HandlePerformanceEndedAsync();
    }

    [Fact]
    public async Task HostOnly_NeverFiresOnItsOwn()
    {
        ConfigureVenue(AdTriggerMode.HostOnly);

        await PerformancesAsync(10);

        await _playback.DidNotReceive().PlayAdAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task PlayNowAsync_WithHostOnly_StillPlays()
    {
        ConfigureVenue(AdTriggerMode.HostOnly);

        Assert.True(await _service.PlayNowAsync());

        await _playback.Received(1).PlayAdAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task PlayNowAsync_WithNoPlaylistChosen_PlaysNothing()
    {
        ConfigureVenue(AdTriggerMode.HostOnly, withPool: false);

        Assert.False(await _service.PlayNowAsync());

        await _playback.DidNotReceive().PlayAdAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task EveryNPerformances_DoesNotFireEarly()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances, interval: 3);

        await PerformancesAsync(2);

        await _playback.DidNotReceive().PlayAdAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task EveryNPerformances_FiresOnTheNth()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances, interval: 3);

        await PerformancesAsync(3);

        await _playback.Received(1).PlayAdAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task EveryNPerformances_ResetsAndFiresAgainOnTheNextRun()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances, interval: 2);

        await PerformancesAsync(4);

        await _playback.Received(2).PlayAdAsync(Arg.Any<Media>());
    }

    // A zero interval reads the same either way here — the counter is already 1 by the time it
    // is compared. The clamp that matters is on the minutes trigger below.
    [Fact]
    public async Task EveryNPerformances_WithAZeroInterval_FiresEveryGap()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances, interval: 0);

        await PerformancesAsync(2);

        await _playback.Received(2).PlayAdAsync(Arg.Any<Media>());
    }

    // The clock starts at initialization, so the first ad waits its interval rather than firing
    // into the first gap of the night.
    [Fact]
    public async Task EveryNMinutes_DoesNotFireBeforeTheIntervalHasPassed()
    {
        ConfigureVenue(AdTriggerMode.EveryNMinutes, interval: 20);
        await _service.InitializeAsync();

        _clock.Advance(TimeSpan.FromMinutes(19));
        await PerformancesAsync(5);

        await _playback.DidNotReceive().PlayAdAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task EveryNMinutes_FiresAtTheNextGapAfterTheInterval()
    {
        ConfigureVenue(AdTriggerMode.EveryNMinutes, interval: 20);
        await _service.InitializeAsync();

        _clock.Advance(TimeSpan.FromMinutes(21));
        await PerformancesAsync(1);

        await _playback.Received(1).PlayAdAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task EveryNMinutes_DoesNotFireTwiceInTheSameWindow()
    {
        ConfigureVenue(AdTriggerMode.EveryNMinutes, interval: 20);
        await _service.InitializeAsync();

        _clock.Advance(TimeSpan.FromMinutes(21));
        await PerformancesAsync(3);

        await _playback.Received(1).PlayAdAsync(Arg.Any<Media>());
    }

    // Unclamped, TimeSpan.FromMinutes(0) is always elapsed, so a hand-edited zero would put an
    // ad in every single gap all night.
    [Fact]
    public async Task EveryNMinutes_WithAZeroInterval_StillWaitsAMinute()
    {
        ConfigureVenue(AdTriggerMode.EveryNMinutes, interval: 0);
        await _service.InitializeAsync();

        await PerformancesAsync(3);

        await _playback.DidNotReceive().PlayAdAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task EveryNMinutes_WithAZeroInterval_FiresOnceTheClampedMinuteHasPassed()
    {
        ConfigureVenue(AdTriggerMode.EveryNMinutes, interval: 0);
        await _service.InitializeAsync();

        _clock.Advance(TimeSpan.FromMinutes(2));
        await PerformancesAsync(1);

        await _playback.Received(1).PlayAdAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task OnIdle_WithSingersStillWaiting_DoesNotFire()
    {
        ConfigureVenue(AdTriggerMode.OnIdle);
        _queue.Users.Returns([new KHostUser { Name = "Alice" }]);

        await PerformancesAsync(1);

        await _playback.DidNotReceive().PlayAdAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task OnIdle_WithAnEmptyQueue_Fires()
    {
        ConfigureVenue(AdTriggerMode.OnIdle);

        await PerformancesAsync(1);

        await _playback.Received(1).PlayAdAsync(Arg.Any<Media>());
    }

    // A refusal leaves the slot due, so the next gap tries again rather than skipping it.
    [Fact]
    public async Task ARefusedAd_LeavesTheCounterDue()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances, interval: 2);
        _adPlays = false;

        await PerformancesAsync(2);
        Assert.Equal(2, _service.PerformancesSinceLastAd);

        _adPlays = true;
        await PerformancesAsync(1);

        // Two attempts, not three: the refusal left the slot due, so the very next gap retried it
        // rather than waiting another full interval.
        await _playback.Received(2).PlayAdAsync(Arg.Any<Media>());
        Assert.Equal(0, _service.PerformancesSinceLastAd);
    }

    [Fact]
    public async Task AnEmptyPlaylist_PlaysNothingAndLeavesTheCounterDue()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances, interval: 1);
        _pools.SelectNextAsync(_poolId, Arg.Any<Guid?>()).Returns(Task.FromResult<Guid?>(null));

        await PerformancesAsync(1);

        await _playback.DidNotReceive().PlayAdAsync(Arg.Any<Media>());
        Assert.Equal(1, _service.PerformancesSinceLastAd);
    }

    [Fact]
    public async Task AFailingPlaylistRead_DoesNotThrowIntoTheQueue()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances, interval: 1);
        _pools.ReadWithEntriesAsync(_poolId).Returns<Task<MediaPool?>>(_ => throw new InvalidOperationException("boom"));

        // The exception must stop here: it is raised inside the gap after a performance, and the
        // next singer is waiting on that gap finishing.
        await PerformancesAsync(1);

        await _playback.DidNotReceive().PlayAdAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task AnAdPlaying_RaisesStateChanged()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances, interval: 1);

        var raised = 0;
        _service.StateChanged += (_, _) => raised++;

        await PerformancesAsync(1);

        Assert.True(raised > 0);
    }

    [Fact]
    public async Task PerformanceEndedOnPlayback_ReachesTheService()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances, interval: 1);

        var gap = new PerformanceEndedEventArgs();
        _playback.PerformanceEnded += Raise.EventWith(gap);
        await gap.WhenFilledAsync();

        await _playback.Received(1).PlayAdAsync(Arg.Any<Media>());
    }

    // Playback holds break music down until the gap's work finishes, so an ad that registered
    // nothing would start underneath a bed that had already come back.
    [Fact]
    public async Task PerformanceEnded_RegistersItsWorkOnTheGap()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances, interval: 1);

        var gap = new PerformanceEndedEventArgs();
        _playback.PerformanceEnded += Raise.EventWith(gap);

        await gap.WhenFilledAsync();

        await _playback.Received(1).PlayAdAsync(Arg.Any<Media>());
    }
}
