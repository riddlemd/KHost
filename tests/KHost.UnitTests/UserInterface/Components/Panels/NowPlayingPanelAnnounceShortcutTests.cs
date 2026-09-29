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

/// <summary>Global Ctrl/Cmd+Shift+Enter clicks this exact button (<c>shortcuts.js</c>); these tests
/// cover what the click does, not the document-level key matching, which is a plain JS concern.</summary>
public class NowPlayingPanelAnnounceShortcutTests : BunitContext
{
    private const string AnnounceSelector = "[data-kh-shortcut='announce-next-singer']";

    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly INextSingerCardService _nextSingerCard = Substitute.For<INextSingerCardService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public NowPlayingPanelAnnounceShortcutTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        // No song loaded: only then does the console offer the announce button at all.
        _playback.CurrentPerformance.Returns((Performance?)null);
        _playback.CurrentMedia.Returns((Media?)null);
        _playback.State.Returns(PlaybackState.Stopped);

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
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<ITimedLyricsService>());
        Services.AddSingleton(Substitute.For<IBreakMusicService>());
        Services.AddSingleton(Substitute.For<IAdService>());
    }

    [Fact]
    public void SomebodyQueued_ClickingAnnounces()
    {
        _queue.Users.Returns([new KHostUser { Id = Guid.NewGuid(), Name = "Ann" }]);
        _nextSingerCard.AnnounceAsync().Returns(true);

        var panel = Render<NowPlayingPanel>();
        panel.Find(AnnounceSelector).Click();

        _nextSingerCard.Received(1).AnnounceAsync();
    }

    /// <summary>Mirrors the button exactly: nobody queued means nobody to announce, and the
    /// button-native disabled attribute is what makes the hotkey a no-op.</summary>
    [Fact]
    public void NobodyQueued_TheTargetIsDisabled()
    {
        _queue.Users.Returns([]);

        var panel = Render<NowPlayingPanel>();

        Assert.True(panel.Find(AnnounceSelector).HasAttribute("disabled"));
    }

    /// <summary>A song already loaded takes the button off the screen entirely, not merely
    /// disabled: announcing over somebody's performance would take their words off the screen.</summary>
    [Fact]
    public void ASongIsLoaded_TheTargetDoesNotExist()
    {
        _playback.CurrentPerformance.Returns(new Performance { Id = Guid.NewGuid() });
        _playback.CurrentMedia.Returns(new Media { FilePath = "song.mp4", Title = "Song" });
        _queue.Users.Returns([new KHostUser { Id = Guid.NewGuid(), Name = "Ann" }]);

        var panel = Render<NowPlayingPanel>();

        Assert.Empty(panel.FindAll(AnnounceSelector));
    }
}
