using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common;
using KHost.Domain.Services.Ads;
using KHost.Domain.Services.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
    private readonly AdService.ServiceOptions _options = new();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private bool _adPlays = true;
    private readonly AdService _service;

    private readonly Guid _poolId = Guid.NewGuid();
    private readonly Guid _venueId = Guid.NewGuid();

    public AdServiceTests()
    {
        // A flag rather than re-stubbing mid-test: NSubstitute treats a second Returns on a call
        // that has already run as a new invocation.
        _playback.PlayAdAsync(Arg.Any<AdPlayback>()).Returns(_ => Task.FromResult(_adPlays));
        _queue.Users.Returns([]);

        _service = new AdService(
            NullLogger<AdService>.Instance, _playback, _pools, _media, _venues, _queue, _clock,
            Options.Create(_options), _broker);
    }

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    private MediaPool ConfigureVenue(AdTriggerMode trigger, int interval = 3, bool withPool = true)
    {
        var pool = new MediaPool { Id = _poolId, Name = "Spots", Purpose = PoolPurpose.Ads, AdTrigger = trigger, AdTriggerInterval = interval };

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
            Type = MediaType.Video,
            Duration = TimeSpan.FromSeconds(20),
        };

        _pools.SelectNextAsync(_poolId, Arg.Any<Guid?>())
            .Returns(Task.FromResult<MediaPoolEntry?>(new MediaPoolEntry { MediaId = media.Id }));
        _media.ReadAsync(media.Id).Returns(Task.FromResult<Media?>(media));

        return pool;
    }

    /// <summary>
    /// The button to play one hangs off this. A host turning ads on mid-shift saw nothing until a
    /// song had finished, because the service worked it out at startup and then never again.
    /// </summary>
    [Fact]
    public async Task IsConfigured_AdsTurnedOnAfterStartup_PicksItUpWithoutWaitingForASongToEnd()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances, withPool: false);
        await _service.InitializeAsync();

        Assert.False(_service.IsConfigured);

        ConfigureVenue(AdTriggerMode.EveryNPerformances);

        await _broker.PublishAsync(new SelectedVenueChanged());

        Assert.True(_service.IsConfigured);
    }

    [Fact]
    public async Task IsConfigured_AdsTurnedOnAfterStartup_TellsTheConsoleToRedraw()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances, withPool: false);
        await _service.InitializeAsync();

        var raised = 0;
        using var subscription = _broker.Subscribe<AdsChanged>(_ => raised++);

        ConfigureVenue(AdTriggerMode.EveryNPerformances);
        await _broker.PublishAsync(new SelectedVenueChanged());

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task IsConfigured_AdsTurnedOffAfterStartup_LosesTheButton()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances);
        await _service.InitializeAsync();

        Assert.True(_service.IsConfigured);

        ConfigureVenue(AdTriggerMode.EveryNPerformances, withPool: false);
        await _broker.PublishAsync(new SelectedVenueChanged());

        Assert.False(_service.IsConfigured);
    }

    /// <summary>Switching between two venues that both run ads is not a reason to redraw.</summary>
    [Fact]
    public async Task IsConfigured_AVenueChangeThatChangesNothing_SaysNothing()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances);
        await _service.InitializeAsync();

        var raised = 0;
        using var subscription = _broker.Subscribe<AdsChanged>(_ => raised++);

        await _broker.PublishAsync(new SelectedVenueChanged());

        Assert.Equal(0, raised);
    }

    /// <summary>
    /// A playlist deleted out from under the venue leaves its id behind. Startup used to take the
    /// id at its word and offer a button that could not play anything, while the check after a
    /// performance resolved the playlist properly — so the button appeared and then vanished.
    /// </summary>
    [Fact]
    public async Task IsConfigured_ThePlaylistTheVenueNamesIsGone_IsFalseFromStartup()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances);
        _pools.ReadWithEntriesAsync(_poolId).Returns(Task.FromResult<MediaPool?>(null));

        await _service.InitializeAsync();

        Assert.False(_service.IsConfigured);
    }

    /// <summary>
    /// A still stamps a nominal length on its media row at import. Reading that would outrank the
    /// configured default and leave the setting looking like it did nothing.
    /// </summary>
    [Fact]
    public async Task ComposeAsync_AStillWithNothingToHear_RunsForTheConfiguredDefault()
    {
        _options.DefaultDuration = TimeSpan.FromSeconds(10);

        var still = Library("card.png", "PNG", TimeSpan.FromSeconds(15));
        var entry = new MediaPoolEntry { MediaId = still.Id };

        var ad = await _service.ComposeAsync(entry);

        Assert.Equal(TimeSpan.FromSeconds(10), ad!.Duration);
    }

    [Fact]
    public async Task ComposeAsync_TheDefaultWasChanged_TheStillFollowsIt()
    {
        _options.DefaultDuration = TimeSpan.FromSeconds(30);

        var still = Library("card.png", "PNG", null);

        var ad = await _service.ComposeAsync(new MediaPoolEntry { MediaId = still.Id });

        Assert.Equal(TimeSpan.FromSeconds(30), ad!.Duration);
    }

    /// <summary>A video answers for itself whatever the default says.</summary>
    [Fact]
    public async Task ComposeAsync_AVideo_RunsForItsOwnLength()
    {
        _options.DefaultDuration = TimeSpan.FromSeconds(10);

        var video = Library("spot.mp4", "MP4", TimeSpan.FromSeconds(22));

        var ad = await _service.ComposeAsync(new MediaPoolEntry { MediaId = video.Id });

        Assert.Equal(TimeSpan.FromSeconds(22), ad!.Duration);
    }

    [Fact]
    public async Task ComposeAsync_TheEntrySaysHowLong_ThatWinsOverEverything()
    {
        _options.DefaultDuration = TimeSpan.FromSeconds(10);

        var video = Library("spot.mp4", "MP4", TimeSpan.FromSeconds(22));
        var entry = new MediaPoolEntry { MediaId = video.Id, Duration = TimeSpan.FromSeconds(7) };

        var ad = await _service.ComposeAsync(entry);

        Assert.Equal(TimeSpan.FromSeconds(7), ad!.Duration);
    }

    /// <summary>The picture and the words still end together — the default does not cut a voiceover.</summary>
    [Fact]
    public async Task ComposeAsync_AStillWithAVoiceover_RunsForWhatIsLeftOfTheVoiceover()
    {
        _options.DefaultDuration = TimeSpan.FromSeconds(10);

        var still = Library("card.png", "PNG", TimeSpan.FromSeconds(15));
        var voice = Library("voice.mp3", "MP3", TimeSpan.FromSeconds(25));

        var ad = await _service.ComposeAsync(new MediaPoolEntry
        {
            MediaId = still.Id,
            AudioMediaId = voice.Id,
            AudioStart = TimeSpan.FromSeconds(5),
        });

        Assert.Equal(TimeSpan.FromSeconds(20), ad!.Duration);
    }

    private async Task PerformancesAsync(int count)
    {
        for (var i = 0; i < count; i++)
            await _service.HandlePerformanceEndedAsync();
    }

    private Media Library(string title, string format, TimeSpan? duration)
    {
        var media = new Media
        {
            Id = Guid.NewGuid(),
            FilePath = $"/media/{title}",
            Title = title,
            Status = MediaStatus.Ready,
            Type = MediaType.Video,
            Format = format,
            Duration = duration,
        };

        _media.ReadAsync(media.Id).Returns(Task.FromResult<Media?>(media));
        return media;
    }

    [Fact]
    public async Task ComposeAsync_AVideoEntry_RunsForTheVideosOwnLength()
    {
        var video = Library("spot.mp4", "MP4", TimeSpan.FromSeconds(30));

        var ad = await _service.ComposeAsync(new MediaPoolEntry { MediaId = video.Id });

        Assert.Equal(TimeSpan.FromSeconds(30), ad?.Duration);
        Assert.True(ad?.HasOwnAudio);
    }

    // A silent card: break music keeps playing under it, which is what HasOwnAudio decides.
    [Fact]
    public async Task ComposeAsync_AStillWithNoAudio_HasNoAudioOfItsOwn()
    {
        var still = Library("card.png", "PNG", MediaFormats.DefaultImageDuration);

        var ad = await _service.ComposeAsync(new MediaPoolEntry { MediaId = still.Id });

        Assert.False(ad?.HasOwnAudio);

        // Its own stamped length is deliberately passed over for the configured default.
        Assert.Equal(_options.DefaultDuration, ad?.Duration);
    }

    [Fact]
    public async Task ComposeAsync_AStillWithAVoiceover_TakesTheRoom()
    {
        var still = Library("card.png", "PNG", MediaFormats.DefaultImageDuration);
        var voice = Library("voice.mp3", "MP3", TimeSpan.FromSeconds(12));

        var ad = await _service.ComposeAsync(new MediaPoolEntry { MediaId = still.Id, AudioMediaId = voice.Id });

        Assert.True(ad?.HasOwnAudio);
    }

    // The picture and the words should end together, so a still takes its length from the clip
    // rather than from the fifteen-second default.
    [Fact]
    public async Task ComposeAsync_AStillWithAVoiceover_RunsForTheVoiceover()
    {
        var still = Library("card.png", "PNG", MediaFormats.DefaultImageDuration);
        var voice = Library("voice.mp3", "MP3", TimeSpan.FromSeconds(12));

        var ad = await _service.ComposeAsync(new MediaPoolEntry { MediaId = still.Id, AudioMediaId = voice.Id });

        Assert.Equal(TimeSpan.FromSeconds(12), ad?.Duration);
    }

    [Fact]
    public async Task ComposeAsync_AnAudioSegment_RunsForWhatIsLeftOfTheClip()
    {
        var voice = Library("bed.mp3", "MP3", TimeSpan.FromMinutes(5));

        var ad = await _service.ComposeAsync(new MediaPoolEntry
        {
            AudioMediaId = voice.Id,
            AudioStart = TimeSpan.FromMinutes(4),
        });

        Assert.Equal(TimeSpan.FromMinutes(1), ad?.Duration);
        Assert.Equal(TimeSpan.FromMinutes(4), ad?.AudioStart);
    }

    [Fact]
    public async Task ComposeAsync_AnExplicitDuration_WinsOverTheFiles()
    {
        var voice = Library("bed.mp3", "MP3", TimeSpan.FromMinutes(5));

        var ad = await _service.ComposeAsync(new MediaPoolEntry
        {
            AudioMediaId = voice.Id,
            AudioStart = TimeSpan.FromMinutes(1),
            Duration = TimeSpan.FromSeconds(8),
        });

        Assert.Equal(TimeSpan.FromSeconds(8), ad?.Duration);
    }

    [Fact]
    public async Task ComposeAsync_AnEntryPointingAtNothing_IsNull()
        => Assert.Null(await _service.ComposeAsync(new MediaPoolEntry()));

    [Fact]
    public async Task ComposeAsync_AVideoWithNoDuration_IsNull()
    {
        var video = Library("spot.mp4", "MP4", duration: null);

        Assert.Null(await _service.ComposeAsync(new MediaPoolEntry { MediaId = video.Id }));
    }

    // A start past the end of the clip leaves nothing to play.
    [Fact]
    public async Task ComposeAsync_AnAudioStartBeyondTheClip_IsNull()
    {
        var voice = Library("bed.mp3", "MP3", TimeSpan.FromSeconds(10));

        Assert.Null(await _service.ComposeAsync(new MediaPoolEntry
        {
            AudioMediaId = voice.Id,
            AudioStart = TimeSpan.FromSeconds(30),
        }));
    }

    [Fact]
    public async Task HostOnly_NeverFiresOnItsOwn()
    {
        ConfigureVenue(AdTriggerMode.HostOnly);

        await PerformancesAsync(10);

        await _playback.DidNotReceive().PlayAdAsync(Arg.Any<AdPlayback>());
    }

    [Fact]
    public async Task PlayNowAsync_WithHostOnly_StillPlays()
    {
        ConfigureVenue(AdTriggerMode.HostOnly);

        Assert.True(await _service.PlayNowAsync());

        await _playback.Received(1).PlayAdAsync(Arg.Any<AdPlayback>());
    }

    [Fact]
    public async Task PlayNowAsync_WithNoPlaylistChosen_PlaysNothing()
    {
        ConfigureVenue(AdTriggerMode.HostOnly, withPool: false);

        Assert.False(await _service.PlayNowAsync());

        await _playback.DidNotReceive().PlayAdAsync(Arg.Any<AdPlayback>());
    }

    [Fact]
    public async Task EveryNPerformances_DoesNotFireEarly()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances, interval: 3);

        await PerformancesAsync(2);

        await _playback.DidNotReceive().PlayAdAsync(Arg.Any<AdPlayback>());
    }

    [Fact]
    public async Task EveryNPerformances_FiresOnTheNth()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances, interval: 3);

        await PerformancesAsync(3);

        await _playback.Received(1).PlayAdAsync(Arg.Any<AdPlayback>());
    }

    [Fact]
    public async Task EveryNPerformances_ResetsAndFiresAgainOnTheNextRun()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances, interval: 2);

        await PerformancesAsync(4);

        await _playback.Received(2).PlayAdAsync(Arg.Any<AdPlayback>());
    }

    // A zero interval reads the same either way here — the counter is already 1 by the time it
    // is compared. The clamp that matters is on the minutes trigger below.
    [Fact]
    public async Task EveryNPerformances_WithAZeroInterval_FiresEveryGap()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances, interval: 0);

        await PerformancesAsync(2);

        await _playback.Received(2).PlayAdAsync(Arg.Any<AdPlayback>());
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

        await _playback.DidNotReceive().PlayAdAsync(Arg.Any<AdPlayback>());
    }

    [Fact]
    public async Task EveryNMinutes_FiresAtTheNextGapAfterTheInterval()
    {
        ConfigureVenue(AdTriggerMode.EveryNMinutes, interval: 20);
        await _service.InitializeAsync();

        _clock.Advance(TimeSpan.FromMinutes(21));
        await PerformancesAsync(1);

        await _playback.Received(1).PlayAdAsync(Arg.Any<AdPlayback>());
    }

    [Fact]
    public async Task EveryNMinutes_NeverPlayedYet_IsDueAtTheFirstGap()
    {
        // Startup calls InitializeAsync, so the clock is normally already stamped and this branch
        // does not come up. It is what the mode falls back to if that ordering ever changes:
        // treat "never played" as due rather than waiting on a null.
        ConfigureVenue(AdTriggerMode.EveryNMinutes, interval: 20);

        Assert.Null(_service.LastAdAtUtc);

        await PerformancesAsync(1);

        await _playback.Received(1).PlayAdAsync(Arg.Any<AdPlayback>());
    }

    [Fact]
    public async Task EveryNMinutes_FiresOnTheIntervalItself()
    {
        // Exactly the interval, not a minute past it: every other test clears the boundary by a
        // margin, which leaves > and >= indistinguishable and an off-by-one free to ship.
        ConfigureVenue(AdTriggerMode.EveryNMinutes, interval: 20);
        await _service.InitializeAsync();

        _clock.Advance(TimeSpan.FromMinutes(20));
        await PerformancesAsync(1);

        await _playback.Received(1).PlayAdAsync(Arg.Any<AdPlayback>());
    }

    [Fact]
    public async Task EveryNMinutes_AnInstantBeforeTheInterval_DoesNotFire()
    {
        ConfigureVenue(AdTriggerMode.EveryNMinutes, interval: 20);
        await _service.InitializeAsync();

        _clock.Advance(TimeSpan.FromMinutes(20) - TimeSpan.FromTicks(1));
        await PerformancesAsync(1);

        await _playback.DidNotReceive().PlayAdAsync(Arg.Any<AdPlayback>());
    }

    [Fact]
    public async Task EveryNMinutes_DoesNotFireTwiceInTheSameWindow()
    {
        ConfigureVenue(AdTriggerMode.EveryNMinutes, interval: 20);
        await _service.InitializeAsync();

        _clock.Advance(TimeSpan.FromMinutes(21));
        await PerformancesAsync(3);

        await _playback.Received(1).PlayAdAsync(Arg.Any<AdPlayback>());
    }

    // Unclamped, TimeSpan.FromMinutes(0) is always elapsed, so a hand-edited zero would put an
    // ad in every single gap all night.
    [Fact]
    public async Task EveryNMinutes_WithAZeroInterval_StillWaitsAMinute()
    {
        ConfigureVenue(AdTriggerMode.EveryNMinutes, interval: 0);
        await _service.InitializeAsync();

        await PerformancesAsync(3);

        await _playback.DidNotReceive().PlayAdAsync(Arg.Any<AdPlayback>());
    }

    [Fact]
    public async Task EveryNMinutes_WithAZeroInterval_FiresOnceTheClampedMinuteHasPassed()
    {
        ConfigureVenue(AdTriggerMode.EveryNMinutes, interval: 0);
        await _service.InitializeAsync();

        _clock.Advance(TimeSpan.FromMinutes(2));
        await PerformancesAsync(1);

        await _playback.Received(1).PlayAdAsync(Arg.Any<AdPlayback>());
    }

    [Fact]
    public async Task OnIdle_WithSingersStillWaiting_DoesNotFire()
    {
        ConfigureVenue(AdTriggerMode.OnIdle);
        _queue.Users.Returns([new KHostUser { Name = "Alice" }]);

        await PerformancesAsync(1);

        await _playback.DidNotReceive().PlayAdAsync(Arg.Any<AdPlayback>());
    }

    [Fact]
    public async Task OnIdle_WithAnEmptyQueue_Fires()
    {
        ConfigureVenue(AdTriggerMode.OnIdle);

        await PerformancesAsync(1);

        await _playback.Received(1).PlayAdAsync(Arg.Any<AdPlayback>());
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
        await _playback.Received(2).PlayAdAsync(Arg.Any<AdPlayback>());
        Assert.Equal(0, _service.PerformancesSinceLastAd);
    }

    [Fact]
    public async Task AnEmptyPlaylist_PlaysNothingAndLeavesTheCounterDue()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances, interval: 1);
        _pools.SelectNextAsync(_poolId, Arg.Any<Guid?>()).Returns(Task.FromResult<MediaPoolEntry?>(null));

        await PerformancesAsync(1);

        await _playback.DidNotReceive().PlayAdAsync(Arg.Any<AdPlayback>());
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

        await _playback.DidNotReceive().PlayAdAsync(Arg.Any<AdPlayback>());
    }

    [Fact]
    public async Task AnAdPlaying_AnnouncesAdsChanged()
    {
        ConfigureVenue(AdTriggerMode.EveryNPerformances, interval: 1);

        var raised = 0;
        using var subscription = _broker.Subscribe<AdsChanged>(_ => raised++);

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

        await _playback.Received(1).PlayAdAsync(Arg.Any<AdPlayback>());
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

        await _playback.Received(1).PlayAdAsync(Arg.Any<AdPlayback>());
    }

    [Fact]
    public async Task PerformanceEnded_TheGapWaitsForTheAdToStart()
    {
        // WhenFilledAsync completes immediately when nothing registered, so asserting the ad
        // played after awaiting it passes whether or not the work was ever handed to the gap.
        // Holding PlayAdAsync open is what tells the two apart — and break music coming up over
        // an ad is exactly what an unheld gap would cause.
        var started = new TaskCompletionSource();
        var finish = new TaskCompletionSource<bool>();

        _playback.PlayAdAsync(Arg.Any<AdPlayback>()).Returns(_ =>
        {
            started.TrySetResult();
            return finish.Task;
        });

        ConfigureVenue(AdTriggerMode.EveryNPerformances, interval: 1);

        var gap = new PerformanceEndedEventArgs();
        _playback.PerformanceEnded += Raise.EventWith(gap);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var filled = gap.WhenFilledAsync();

        Assert.False(filled.IsCompleted, "the gap released before the ad had started");

        finish.SetResult(true);

        await filled.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
