using Microsoft.Extensions.Logging.Abstractions;
using KHost.Abstractions.Messaging;
using KHost.Domain.Services.Messaging;
using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>Global Ctrl/Cmd+Enter clicks this exact button (<c>shortcuts.js</c>); these tests cover
/// what the click does, not the document-level key matching, which is a plain JS concern. The
/// target names the rotation's own head — <see cref="ISingerQueueService.Users"/>'s first entry —
/// never whoever the console has selected, which is why several tests below select someone else
/// entirely and still expect singer #1's song to play.</summary>
public class NowPlayingPanelPlayNextShortcutTests : BunitContext
{
    private const string PlayNextSelector = "[data-kh-shortcut='play-next']";

    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly INextSingerCardService _nextSingerCard = Substitute.For<INextSingerCardService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    private readonly KHostUser _first = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly KHostUser _second = new() { Id = Guid.NewGuid(), Name = "Bea" };
    private readonly Performance _performance;
    private readonly Media _media;

    public NowPlayingPanelPlayNextShortcutTests()
    {
        _performance = new Performance { Id = Guid.NewGuid(), SingerId = _first.Id, MediaId = Guid.NewGuid() };
        _media = new Media { Id = _performance.MediaId, FilePath = "/music/song.mp4", Title = "Song", Status = MediaStatus.Ready };

        JSInterop.Mode = JSRuntimeMode.Loose;

        // No song loaded: only then does the console offer the idle-state buttons at all.
        _playback.CurrentPerformance.Returns((Performance?)null);
        _playback.CurrentMedia.Returns((Media?)null);
        _playback.State.Returns(PlaybackState.Stopped);
        _playback.HasConnectedScreenAsync().Returns(true);

        _queue.Users.Returns([_first, _second]);
        _performances.ReadSingersNextPerformanceAsync(_first.Id).Returns(_performance);
        _mediaService.ReadAsync(_performance.MediaId).Returns(_media);

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());

        var venues = Substitute.For<IVenuesService>();
        venues.ReadSelectedVenueAsync().Returns((Venue?)null);

        Services.AddSingleton(venues);
        Services.AddSingleton(_playback);
        Services.AddSingleton(_nextSingerCard);
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(appSettings);
        Services.AddSingleton<IMessageBroker>(_broker);
        Services.AddSingleton(_queue);
        Services.AddSingleton(_performances);
        Services.AddSingleton(_mediaService);
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<ITimedLyricsService>());
        Services.AddSingleton(Substitute.For<IBreakMusicService>());
        Services.AddSingleton(Substitute.For<IAdService>());
    }

    [Fact]
    public void RendersUnderTheAnnounceButtonAndCarriesTheShortcutTarget()
    {
        var panel = Render<NowPlayingPanel>();

        var actions = panel.Find(".kh-now-playing__empty-actions").Children;

        Assert.Equal(2, actions.Length);
        Assert.Equal("announce-next-singer", actions[0].GetAttribute("data-kh-shortcut"));
        Assert.Equal("play-next", actions[1].GetAttribute("data-kh-shortcut"));
    }

    [Fact]
    public void SingerOneSelected_ClickingPlaysSingerOnesFirstSong()
    {
        _queue.SelectedUser.Returns(_first);
        _queue.SelectedUserId.Returns(_first.Id);

        var panel = Render<NowPlayingPanel>();
        panel.Find(PlayNextSelector).Click();

        _playback.Received(1).LoadAsync(_performance, _media);
        _playback.Received(1).PlayAsync();
    }

    [Fact]
    public void SingerTwoSelected_ClickingStillPlaysSingerOnesFirstSong()
    {
        _queue.SelectedUser.Returns(_second);
        _queue.SelectedUserId.Returns(_second.Id);

        var panel = Render<NowPlayingPanel>();
        panel.Find(PlayNextSelector).Click();

        _playback.Received(1).LoadAsync(_performance, _media);
        _playback.Received(1).PlayAsync();
    }

    [Fact]
    public void NobodySelected_ClickingStillPlaysSingerOnesFirstSong()
    {
        _queue.SelectedUser.Returns((KHostUser?)null);
        _queue.SelectedUserId.Returns((Guid?)null);

        var panel = Render<NowPlayingPanel>();
        panel.Find(PlayNextSelector).Click();

        _playback.Received(1).LoadAsync(_performance, _media);
        _playback.Received(1).PlayAsync();
    }

    /// <summary>A song already loaded takes the whole idle block off the screen, target included:
    /// the same reasoning the announce button's absence test uses.</summary>
    [Fact]
    public void ASongIsLoaded_TheTargetDoesNotExistAndClickingWouldDoNothing()
    {
        _playback.CurrentPerformance.Returns(new Performance { Id = Guid.NewGuid() });
        _playback.CurrentMedia.Returns(new Media { FilePath = "other.mp4", Title = "Other" });

        var panel = Render<NowPlayingPanel>();

        Assert.Empty(panel.FindAll(PlayNextSelector));
    }

    /// <summary>The top singer's own top song still downloading must not fall through to playing
    /// nothing, or a later, ready song: "next" means their first queued song, full stop.</summary>
    [Fact]
    public void TopSingersTopSongNotReady_TheTargetIsDisabled()
    {
        _media.Status = MediaStatus.Downloading;

        var panel = Render<NowPlayingPanel>();

        Assert.True(panel.Find(PlayNextSelector).HasAttribute("disabled"));
    }

    [Fact]
    public void TopSingerHasNoSongsQueued_TheTargetIsDisabled()
    {
        _performances.ReadSingersNextPerformanceAsync(_first.Id).Returns((Performance?)null);

        var panel = Render<NowPlayingPanel>();

        Assert.True(panel.Find(PlayNextSelector).HasAttribute("disabled"));
    }

    [Fact]
    public void NobodyInTheQueue_TheTargetIsDisabled()
    {
        _queue.Users.Returns([]);

        var panel = Render<NowPlayingPanel>();

        Assert.True(panel.Find(PlayNextSelector).HasAttribute("disabled"));
    }
}
