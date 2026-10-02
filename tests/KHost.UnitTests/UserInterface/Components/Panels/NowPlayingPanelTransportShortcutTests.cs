using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>Ctrl/Cmd+Alt+Enter and Ctrl/Cmd+Backspace click these buttons (<c>shortcuts.js</c>), so the
/// hook has to sit on the button that does the work, and a disabled stop must read as disabled.</summary>
public class NowPlayingPanelTransportShortcutTests : BunitContext
{
    private const string PauseResumeSelector = "[data-kh-shortcut='pause-resume']";
    private const string StopSelector = "[data-kh-shortcut='stop']";

    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();

    public NowPlayingPanelTransportShortcutTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _playback.Position.Returns(TimeSpan.Zero);
        _playback.CurrentPerformance.Returns(new Performance { SingerId = Guid.NewGuid() });
        _playback.CurrentMedia.Returns(new Media { FilePath = "song.mp4", Title = "Africa" });

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());

        var venues = Substitute.For<IVenuesService>();
        venues.ReadSelectedVenueAsync().Returns((Venue?)null);

        Services.AddSingleton(venues);
        Services.AddSingleton(_playback);
        Services.AddSingleton(Substitute.For<INextSingerCardService>());
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(appSettings);
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
        Services.AddSingleton(Substitute.For<ISingerQueueService>());
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<IPerformanceService>());
        Services.AddSingleton(Substitute.For<IMediaService>());
        Services.AddSingleton(Substitute.For<ITimedLyricsService>());
        Services.AddSingleton(Substitute.For<IBreakMusicService>());
        Services.AddSingleton(Substitute.For<IAdService>());
    }

    [Fact]
    public void Playing_TheShortcutPauses()
    {
        _playback.State.Returns(PlaybackState.Playing);

        Render<NowPlayingPanel>().Find(PauseResumeSelector).Click();

        _playback.Received(1).PauseAsync();
    }

    [Fact]
    public void Paused_TheShortcutResumes()
    {
        _playback.State.Returns(PlaybackState.Paused);
        _playback.HasConnectedScreenAsync().Returns(true);

        Render<NowPlayingPanel>().Find(PauseResumeSelector).Click();

        _playback.Received(1).PlayAsync();
        _playback.DidNotReceive().PauseAsync();
    }

    [Fact]
    public void Playing_TheStopShortcutStops()
    {
        _playback.State.Returns(PlaybackState.Playing);

        var stop = Render<NowPlayingPanel>().Find(StopSelector);

        Assert.False(stop.HasAttribute("disabled"));
        stop.Click();
        _playback.Received(1).StopAsync();
    }

    /// <summary>shortcuts.js leaves the key alone on a disabled target; this is what it reads.</summary>
    [Fact]
    public void Stopping_TheStopShortcutTargetIsDisabled()
    {
        _playback.State.Returns(PlaybackState.Stopping);

        Assert.True(Render<NowPlayingPanel>().Find(StopSelector).HasAttribute("disabled"));
    }

    [Fact]
    public void NothingLoaded_OffersNeitherTarget()
    {
        _playback.CurrentPerformance.Returns((Performance?)null);
        _playback.CurrentMedia.Returns((Media?)null);
        _playback.State.Returns(PlaybackState.Stopped);

        var panel = Render<NowPlayingPanel>();

        Assert.Empty(panel.FindAll(PauseResumeSelector));
        Assert.Empty(panel.FindAll(StopSelector));
    }
}
