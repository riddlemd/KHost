using KHost.Abstractions.Services;
using KHost.Domain.Services.Playback;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace KHost.UnitTests.Domain.Services.Playback;

public class PlaybackClockTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 20, 0, 0, TimeSpan.Zero));
    private TimeSpan _displayStartTimeout = TimeSpan.Zero;
    private double _rate = 1.0;
    private readonly PlaybackClock _clock;

    public PlaybackClockTests()
    {
        _clock = new PlaybackClock(
            _time,
            NullLogger.Instance,
            () => _displayStartTimeout,
            () => _rate,
            () => Task.CompletedTask);
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private DisplayPlaybackStatus Report(TimeSpan position, DateTime sampledAtUtc)
        => new() { Position = position, IsPlaying = true, SampledAtUtc = sampledAtUtc };

    private long LiveGeneration()
    {
        _clock.Start();
        Assert.True(_clock.TryAdvance(out var generation));
        return generation;
    }

    [Fact]
    public void AClaim_MovesTheGenerationOn()
    {
        var claimed = LiveGeneration();

        Assert.True(_clock.TryClaim(claimed));

        Assert.True(_clock.TryAdvance(out var after));
        Assert.NotEqual(claimed, after);
    }

    [Fact]
    public void AClaimedSong_CannotBeClaimedAgain_UnderAGenerationReadBeforeItsReset()
    {
        Assert.True(_clock.TryClaim(LiveGeneration()));

        Assert.True(_clock.TryAdvance(out var readWhileConcluding));

        Assert.False(_clock.TryClaim(readWhileConcluding));
    }

    [Fact]
    public void Reset_LetsTheNextSongBeClaimed()
    {
        Assert.True(_clock.TryClaim(LiveGeneration()));

        _clock.Reset();

        Assert.True(_clock.TryClaim(LiveGeneration()));
    }

    [Fact]
    public void TryClaim_AGenerationFromBeforeAReset_IsRefused()
    {
        var old = LiveGeneration();

        _clock.Reset();

        Assert.False(_clock.TryClaim(old));
    }

    [Fact]
    public void TryTakeEnd_WhileTheScreenIsHolding_IsRefused()
    {
        _clock.Hold(Report(TimeSpan.Zero, Now));
        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.False(_clock.TryTakeEnd(Report(TimeSpan.FromMinutes(3), Now), out _));
    }

    [Fact]
    public void TryTakeEnd_SampledWithinASecondOfATransportMove_IsRefused_AndASecondOnIsTaken()
    {
        var moved = Now;
        _clock.MarkTransportMoved();

        Assert.False(_clock.TryTakeEnd(Report(TimeSpan.FromMinutes(3), moved + TimeSpan.FromMilliseconds(999)), out _));
        Assert.True(_clock.TryTakeEnd(Report(TimeSpan.FromMinutes(3), moved + TimeSpan.FromSeconds(1)), out _));
    }

    [Fact]
    public void TryTakeEnd_HandsBackTheLiveGeneration()
    {
        _clock.Reset();
        var live = LiveGeneration();

        Assert.True(_clock.TryTakeEnd(Report(TimeSpan.FromMinutes(3), Now), out var generation));

        Assert.Equal(live, generation);
    }

    [Fact]
    public void TryAdvance_AfterTheClockStopped_IsRefused()
    {
        _clock.Start();
        _clock.Stop();

        Assert.False(_clock.TryAdvance(out _));
    }

    [Fact]
    public void TryAdvance_MovesThePlayheadBySongTime_FromTheLastTick()
    {
        _rate = 1.5;
        _clock.Start();

        _time.Advance(TimeSpan.FromSeconds(1));
        _clock.TryAdvance(out _);
        _time.Advance(TimeSpan.FromSeconds(1));
        _clock.TryAdvance(out _);

        Assert.Equal(TimeSpan.FromSeconds(3), _clock.Position);
    }

    [Fact]
    public void TryAdvance_WhileAwaitingTheDisplay_HoldsThePlayhead_UntilTheTimeoutRunsOut()
    {
        _displayStartTimeout = TimeSpan.FromSeconds(5);
        _clock.Start();
        _clock.AwaitDisplayStart();

        _time.Advance(TimeSpan.FromSeconds(4));
        _clock.TryAdvance(out _);
        Assert.Equal(TimeSpan.Zero, _clock.Position);

        _time.Advance(TimeSpan.FromSeconds(1));
        _clock.TryAdvance(out _);
        Assert.Equal(TimeSpan.FromSeconds(1), _clock.Position);
    }

    [Fact]
    public void FollowReport_SampledBeforeThePlayWentOut_IsIgnored()
    {
        _displayStartTimeout = TimeSpan.FromSeconds(5);
        var played = Now;
        _clock.AwaitDisplayStart();

        _clock.FollowReport(Report(TimeSpan.FromSeconds(10), played - TimeSpan.FromSeconds(1)));

        Assert.Equal(TimeSpan.Zero, _clock.Position);
    }

    [Fact]
    public void FollowReport_MovesThePlayheadOn_ByTheSongTimeSinceItWasSampled()
    {
        _rate = 2.0;

        _clock.FollowReport(Report(TimeSpan.FromSeconds(10), Now - TimeSpan.FromSeconds(2)));

        Assert.Equal(TimeSpan.FromSeconds(14), _clock.Position);
    }
}
