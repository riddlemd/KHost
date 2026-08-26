using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Domain.Services.BreakMusic;
using KHost.Domain.Services.Messaging;
using KHost.Plugins.Sdk.Models;
using KHost.Plugins.Sdk.Messaging.Messages;
using KHost.Plugins.Sdk.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services.BreakMusic;

// The whole point of this service is the two automatic transitions. Suspend must respect what the
// host set — a bed they paused on purpose must not come back on the song's behalf — and restore
// must bring back only what suspend took away.
public class BreakMusicServiceTests : IDisposable
{
    private readonly IBreakMusicProvider _provider = Substitute.For<IBreakMusicProvider>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly BreakMusicService _service;

    public BreakMusicServiceTests()
    {
        _provider.SourceName.Returns(nameof(LibraryBreakMusicProvider));
        _provider.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
        _venues.ReadSelectedVenueAsync().Returns(Task.FromResult<Venue?>(null));

        _service = new BreakMusicService(NullLogger<BreakMusicService>.Instance, [_provider], _venues, _broker);
    }

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task PlayingAsync()
    {
        await _service.InitializeAsync();
        await _service.StartAsync();
    }

    [Fact]
    public async Task StartAsync_WhileASongHasTheRoom_IsRefused()
    {
        // The bed was the one thing that could reach the room from the host's own button over a
        // singer; PlaybackService refuses ads the same way.
        await _service.InitializeAsync();
        await _service.SuspendAsync();

        var started = await _service.StartAsync();

        Assert.False(started);
        Assert.NotEqual(BreakMusicState.Playing, _service.State);
        await _provider.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAsync_WhileASongHasTheRoom_IsRefused()
    {
        // The path the button actually takes when the bed was paused. A song loading over a paused
        // bed leaves it paused, not suspended, so Suspend's own state check never fires.
        await PlayingAsync();
        await _service.PauseAsync();
        await _service.SuspendAsync();

        await _service.ResumeAsync();

        Assert.Equal(BreakMusicState.Paused, _service.State);
        await _provider.DidNotReceive().ResumeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SkipAsync_WhileASongHasTheRoom_IsRefused()
    {
        // Skipping starts the next track on every provider, so it reaches the room exactly as
        // Resume does.
        await PlayingAsync();
        await _service.PauseAsync();
        await _service.SuspendAsync();

        await _service.SkipAsync();

        Assert.Equal(BreakMusicState.Paused, _service.State);
        await _provider.DidNotReceive().SkipAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAsync_OnceTheSongIsOver_ResumesNormally()
    {
        await PlayingAsync();
        await _service.PauseAsync();
        await _service.SuspendAsync();
        await _service.RestoreAsync();

        await _service.ResumeAsync();

        Assert.Equal(BreakMusicState.Playing, _service.State);
    }

    [Fact]
    public async Task StartAsync_AfterTheSongHandsTheRoomBack_PlaysAgain()
    {
        await _service.InitializeAsync();
        await _service.SuspendAsync();
        await _service.RestoreAsync();

        Assert.True(await _service.StartAsync());
        Assert.Equal(BreakMusicState.Playing, _service.State);
    }

    [Fact]
    public async Task RestoreAsync_AfterABedThatWasPlaying_BringsItBack()
    {
        // Restore starts the bed through StartAsync, so the refusal has to be lifted before that
        // call rather than after it, or the automatic return never happens.
        await PlayingAsync();
        await _service.SuspendAsync();

        Assert.Equal(BreakMusicState.Suspended, _service.State);

        await _service.RestoreAsync();

        Assert.Equal(BreakMusicState.Playing, _service.State);
    }

    [Fact]
    public void NewService_StartsStopped()
    {
        Assert.Equal(BreakMusicState.Stopped, _service.State);
    }

    [Fact]
    public async Task InitializeAsync_WithNoStoredProvider_FallsBackToTheOnlyOne()
    {
        await _service.InitializeAsync();

        Assert.Same(_provider, _service.ActiveProvider);
    }

    // Plugins register after the domain today, so the built-in happens to be first — but a venue's
    // default must not rest on that. A plugin ahead of it in the list must not become the default.
    [Fact]
    public async Task InitializeAsync_WithAPluginProviderRegisteredFirst_StillDefaultsToTheBuiltIn()
    {
        var plugin = Substitute.For<IBreakMusicProvider>();
        plugin.SourceName.Returns("SpotifyProvider");

        var library = new LibraryBreakMusicProvider(
            NullLogger<LibraryBreakMusicProvider>.Instance,
            Substitute.For<IMediaPoolService>(),
            Substitute.For<IMediaService>(),
            Substitute.For<IMediaStreamService>(),
            Substitute.For<IScreenServer>(),
            Substitute.For<IScreenCoordinationService>(),
            _venues, _broker);

        using var service = new BreakMusicService(
            NullLogger<BreakMusicService>.Instance, [plugin, library], _venues, _broker);

        await service.InitializeAsync();

        Assert.Same(library, service.ActiveProvider);
    }

    [Fact]
    public async Task SetActiveProviderAsync_NamesAPluginProvider_SwitchesToIt()
    {
        var plugin = Substitute.For<IBreakMusicProvider>();
        plugin.SourceName.Returns("SpotifyProvider");

        using var service = new BreakMusicService(
            NullLogger<BreakMusicService>.Instance, [_provider, plugin], _venues, _broker);

        await service.InitializeAsync();
        await service.SetActiveProviderAsync("SpotifyProvider");

        Assert.Same(plugin, service.ActiveProvider);
    }

    [Fact]
    public async Task InitializeAsync_WithAnUnknownStoredProvider_FallsBackRatherThanLeavingNone()
    {
        _venues.ReadSelectedVenueAsync().Returns(Task.FromResult<Venue?>(new Venue
        {
            Name = "The Bar",
            Settings = new Venue.VenueSettings { BreakMusicProvider = "SomePluginThatIsGone" },
        }));

        await _service.InitializeAsync();

        Assert.Same(_provider, _service.ActiveProvider);
    }

    [Fact]
    public async Task StartAsync_WhenTheProviderHasNothingToPlay_StaysStopped()
    {
        _provider.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        await _service.InitializeAsync();

        Assert.False(await _service.StartAsync());
        Assert.Equal(BreakMusicState.Stopped, _service.State);
    }

    // One venue level covers every channel, so a provider the host cannot reach is told it and
    // one that renders through the host is not — ScreenCoordination already sets that channel.
    [Fact]
    public async Task StartAsync_AnExternalProvider_IsGivenTheVenueVolume()
    {
        _provider.RendersThroughHost.Returns(false);
        _venues.ReadSelectedVenueAsync().Returns(Task.FromResult<Venue?>(new Venue
        {
            Name = "The Bar",
            Settings = new Venue.VenueSettings { DefaultVolume = 40 },
        }));

        await _service.InitializeAsync();
        _provider.ClearReceivedCalls();

        await _service.StartAsync();

        await _provider.Received(1).SetVolumeAsync(0.4f, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_AHostRenderedProvider_IsNotGivenAVolume()
    {
        _provider.RendersThroughHost.Returns(true);

        await _service.InitializeAsync();
        _provider.ClearReceivedCalls();

        await _service.StartAsync();

        await _provider.DidNotReceive().SetVolumeAsync(Arg.Any<float>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AVenueEdit_PushesTheNewLevelAtAnExternalProvider()
    {
        _provider.RendersThroughHost.Returns(false);
        _venues.ReadSelectedVenueAsync().Returns(Task.FromResult<Venue?>(new Venue
        {
            Name = "The Bar",
            Settings = new Venue.VenueSettings { DefaultVolume = 25 },
        }));

        await _service.InitializeAsync();
        _provider.ClearReceivedCalls();

        await _broker.PublishAsync(new SelectedVenueChanged());

        await _provider.Received().SetVolumeAsync(0.25f, Arg.Any<CancellationToken>());
    }

    // Editing some other venue's details is not the room's business: pushing the level at an
    // external provider on every venue edit is a volume change the host never asked for.
    [Fact]
    public async Task AnEditToADifferentVenue_LeavesTheLevelAlone()
    {
        _provider.RendersThroughHost.Returns(false);

        await _service.InitializeAsync();
        _provider.ClearReceivedCalls();

        await _broker.PublishAsync(new VenuesChanged());

        await _provider.DidNotReceive().SetVolumeAsync(Arg.Any<float>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PauseAsync_FromPlaying_Pauses()
    {
        await PlayingAsync();

        await _service.PauseAsync();

        Assert.Equal(BreakMusicState.Paused, _service.State);
        await _provider.Received(1).PauseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PauseAsync_WhenStopped_DoesNothing()
    {
        await _service.InitializeAsync();

        await _service.PauseAsync();

        Assert.Equal(BreakMusicState.Stopped, _service.State);
        await _provider.DidNotReceive().PauseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuspendAsync_WhilePlaying_StopsTheProviderWithAFade()
    {
        await PlayingAsync();

        await _service.SuspendAsync();

        Assert.Equal(BreakMusicState.Suspended, _service.State);
        await _provider.Received(1).StopAsync(Arg.Is<TimeSpan?>(f => f > TimeSpan.Zero), Arg.Any<CancellationToken>());
    }

    // The host paused it deliberately, so the song ending must not undo that.
    [Fact]
    public async Task SuspendAsync_WhilePaused_LeavesItPaused()
    {
        await PlayingAsync();
        await _service.PauseAsync();

        await _service.SuspendAsync();

        Assert.Equal(BreakMusicState.Paused, _service.State);
    }

    // A provider driving another app outlives this process. Starting at Stopped left the console
    // saying the bed was off while the room could hear it.
    [Fact]
    public async Task InitializeAsync_ProviderIsAlreadyPlaying_AdoptsThat()
    {
        _provider.ReadPlaybackAsync(Arg.Any<CancellationToken>()).Returns(BreakMusicPlayback.Playing);

        await _service.InitializeAsync();

        Assert.Equal(BreakMusicState.Playing, _service.State);
    }

    [Fact]
    public async Task InitializeAsync_ProviderIsPaused_AdoptsThat()
    {
        _provider.ReadPlaybackAsync(Arg.Any<CancellationToken>()).Returns(BreakMusicPlayback.Paused);

        await _service.InitializeAsync();

        Assert.Equal(BreakMusicState.Paused, _service.State);
    }

    // Null is "cannot tell", which is not the same as "nothing is playing".
    [Fact]
    public async Task InitializeAsync_ProviderCannotTell_LeavesTheStateAlone()
    {
        _provider.ReadPlaybackAsync(Arg.Any<CancellationToken>()).Returns((BreakMusicPlayback?)null);

        await _service.InitializeAsync();

        Assert.Equal(BreakMusicState.Stopped, _service.State);
    }

    // The console must still come up when the other app will not answer.
    [Fact]
    public async Task InitializeAsync_ProviderThrowsOnTheLook_StillInitialises()
    {
        _provider.ReadPlaybackAsync(Arg.Any<CancellationToken>())
            .Returns<BreakMusicPlayback?>(_ => throw new InvalidOperationException("no"));

        await _service.InitializeAsync();

        Assert.Equal(_provider, _service.ActiveProvider);
    }

    // The read runs off the broker's chain, so the announcement that follows it is what says the
    // service has caught up. Waiting on that rather than on a delay keeps this from being a race.
    private async Task PublishProviderMovedAsync()
    {
        var caughtUp = new TaskCompletionSource();
        using var subscription = _broker.Subscribe<BreakMusicChanged>(_ => caughtUp.TrySetResult());

        await _broker.PublishAsync(new BreakMusicTrackChanged(nameof(LibraryBreakMusicProvider)));

        await caughtUp.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // What a host pressing pause in the other app's own window looks like from here: the provider
    // says it moved, and this service asks what it moved to rather than assuming a track change.
    [Fact]
    public async Task ProviderAnnouncesItMoved_TransportChangedToo_FollowsIt()
    {
        await PlayingAsync();
        _provider.ReadPlaybackAsync(Arg.Any<CancellationToken>()).Returns(BreakMusicPlayback.Paused);

        await PublishProviderMovedAsync();

        Assert.Equal(BreakMusicState.Paused, _service.State);
    }

    // Suspended is this service's own doing and the song that caused it is still playing. A
    // provider going quiet underneath must not be read as the host wanting the bed back.
    [Fact]
    public async Task ProviderAnnouncesItMoved_WhileSuspended_StaysSuspended()
    {
        await PlayingAsync();
        await _service.SuspendAsync();
        _provider.ReadPlaybackAsync(Arg.Any<CancellationToken>()).Returns(BreakMusicPlayback.Stopped);

        await PublishProviderMovedAsync();

        Assert.Equal(BreakMusicState.Suspended, _service.State);
    }

    // The song was going to play over the top of music this service never started.
    [Fact]
    public async Task SuspendAsync_ProviderIsPlayingButThisStateSaysStopped_SuspendsAnyway()
    {
        await _service.InitializeAsync();
        _provider.ReadPlaybackAsync(Arg.Any<CancellationToken>()).Returns(BreakMusicPlayback.Playing);

        await _service.SuspendAsync();

        Assert.Equal(BreakMusicState.Suspended, _service.State);
        await _provider.Received(1).StopAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestoreAsync_AfterSuspendingFromPlaying_PlaysAgain()
    {
        await PlayingAsync();
        await _service.SuspendAsync();

        await _service.RestoreAsync();

        Assert.Equal(BreakMusicState.Playing, _service.State);
    }

    [Fact]
    public async Task RestoreAsync_AfterTheHostPausedIt_LeavesItPaused()
    {
        await PlayingAsync();
        await _service.PauseAsync();
        await _service.SuspendAsync();

        await _service.RestoreAsync();

        Assert.Equal(BreakMusicState.Paused, _service.State);
    }

    [Fact]
    public async Task RestoreAsync_WhenItWasNeverSuspended_DoesNotStartIt()
    {
        await _service.InitializeAsync();

        await _service.RestoreAsync();

        Assert.Equal(BreakMusicState.Stopped, _service.State);
        await _provider.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
    }

    // Two songs in a row: the second suspend has nothing to take, so the second restore has
    // nothing to give back either.
    [Fact]
    public async Task RestoreAsync_CalledTwice_OnlyStartsOnce()
    {
        await PlayingAsync();
        await _service.SuspendAsync();

        await _service.RestoreAsync();
        _provider.ClearReceivedCalls();

        await _service.RestoreAsync();

        await _provider.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestoreAsync_WhenThePoolHasRunDry_EndsStoppedRatherThanSuspended()
    {
        await PlayingAsync();
        await _service.SuspendAsync();

        _provider.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        await _service.RestoreAsync();

        Assert.Equal(BreakMusicState.Stopped, _service.State);
    }

    [Fact]
    public async Task StopAsync_ThenRestore_DoesNotBringItBack()
    {
        await PlayingAsync();
        await _service.StopAsync();

        await _service.RestoreAsync();

        Assert.Equal(BreakMusicState.Stopped, _service.State);
    }

    [Fact]
    public async Task SkipAsync_WhenStopped_DoesNothing()
    {
        await _service.InitializeAsync();

        await _service.SkipAsync();

        await _provider.DidNotReceive().SkipAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SkipAsync_WhilePlaying_AsksTheProvider()
    {
        await PlayingAsync();

        await _service.SkipAsync();

        await _provider.Received(1).SkipAsync(Arg.Any<CancellationToken>());
    }

    // Skipping starts the next track whichever provider is on — the library one plays what it
    // loads, and a media-key next resumes a paused Spotify. Staying Paused left the bar offering
    // play while the room could hear music.
    [Fact]
    public async Task SkipAsync_WhilePaused_ReportsPlaying()
    {
        await PlayingAsync();
        await _service.PauseAsync();

        await _service.SkipAsync();

        Assert.Equal(BreakMusicState.Playing, _service.State);
    }

    // Suspended is break music yielding to a singer. Promoting it would claim the bed is playing
    // over the song, and it comes back on its own when the song ends.
    [Fact]
    public async Task SkipAsync_WhileSuspended_StaysSuspended()
    {
        await PlayingAsync();
        await _service.SuspendAsync();

        await _service.SkipAsync();

        Assert.Equal(BreakMusicState.Suspended, _service.State);
    }

    [Fact]
    public async Task StartAsync_AnnouncesBreakMusicChanged()
    {
        await _service.InitializeAsync();

        var raised = 0;
        using var subscription = _broker.Subscribe<BreakMusicChanged>(_ => raised++);

        await _service.StartAsync();

        Assert.Equal(1, raised);
    }

    // Announced off the broker's chain now, so the count is taken once it has actually landed
    // rather than the instant the publish returns.
    [Fact]
    public async Task TrackChangedOnTheActiveProvider_AnnouncesBreakMusicChanged()
    {
        await _service.InitializeAsync();

        var raised = 0;
        var landed = new TaskCompletionSource();
        using var subscription = _broker.Subscribe<BreakMusicChanged>(_ =>
        {
            raised++;
            landed.TrySetResult();
        });

        await _broker.PublishAsync(new BreakMusicTrackChanged(_provider.SourceName));
        await landed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, raised);
    }

    // A provider still winding down must not redraw the console with its own track.
    [Fact]
    public async Task TrackChangedOnAnotherProvider_IsIgnored()
    {
        await _service.InitializeAsync();

        var raised = 0;
        using var subscription = _broker.Subscribe<BreakMusicChanged>(_ => raised++);

        await _broker.PublishAsync(new BreakMusicTrackChanged("SomeOtherProvider"));

        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task CurrentTrack_ComesFromTheActiveProvider()
    {
        _provider.CurrentTrack.Returns(new BreakMusicTrack { Title = "Bed Track", Artist = "Someone" });

        await _service.InitializeAsync();

        Assert.Equal("Bed Track", _service.CurrentTrack?.Title);
    }
}
