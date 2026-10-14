using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
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
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();

    public NowPlayingPanelAnnounceShortcutTests()
    {
        this.AddKHostTestDefaults();

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
        Services.AddSingleton(appSettings);
        Services.AddSingleton(_queue);
        Services.AddSingleton(_performances);
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

    [Fact]
    public void CardUp_TheButtonOffersToHideIt()
    {
        _queue.Users.Returns([new KHostUser { Id = Guid.NewGuid(), Name = "Ann" }]);
        _nextSingerCard.IsAnnounced.Returns(true);

        var button = Render<NowPlayingPanel>().Find(AnnounceSelector);

        Assert.Contains("Hide Next Singer", button.TextContent);
        Assert.Equal("true", button.GetAttribute("aria-pressed"));
        Assert.Equal("Take who is up off the screens (Ctrl+Shift+Enter)", button.GetAttribute("title"));
        Assert.NotNull(button.QuerySelector(".bi-eye-slash"));
    }

    [Fact]
    public void CardDown_TheButtonOffersToAnnounce()
    {
        _queue.Users.Returns([new KHostUser { Id = Guid.NewGuid(), Name = "Ann" }]);

        var button = Render<NowPlayingPanel>().Find(AnnounceSelector);

        Assert.Contains("Announce Next Singer", button.TextContent);
        Assert.Equal("false", button.GetAttribute("aria-pressed"));
    }

    /// <summary>The queue can empty while the card is up; it must still come down.</summary>
    [Fact]
    public void CardUpWithNobodyQueued_TheButtonIsEnabled()
    {
        _queue.Users.Returns([]);
        _nextSingerCard.IsAnnounced.Returns(true);

        var panel = Render<NowPlayingPanel>();

        Assert.False(panel.Find(AnnounceSelector).HasAttribute("disabled"));
    }

    [Fact]
    public void CardUp_ClickingWithdrawsIt_RatherThanAnnouncingAgain()
    {
        _queue.Users.Returns([]);
        _nextSingerCard.IsAnnounced.Returns(true);

        var panel = Render<NowPlayingPanel>();
        panel.Find(AnnounceSelector).Click();

        _nextSingerCard.Received(1).WithdrawAsync(Arg.Any<CancellationToken>());
        _nextSingerCard.DidNotReceive().AnnounceAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>The card can come down without this console's button: the screen drew over it, or
    /// another circuit pressed it.</summary>
    [Fact]
    public async Task NextSingerWithdrawn_RedrawsTheButton()
    {
        _queue.Users.Returns([new KHostUser { Id = Guid.NewGuid(), Name = "Ann" }]);
        _nextSingerCard.IsAnnounced.Returns(true);
        var panel = Render<NowPlayingPanel>();

        _nextSingerCard.IsAnnounced.Returns(false);
        await Services.GetRequiredService<IMessageBroker>().PublishAsync(new NextSingerWithdrawn());

        panel.WaitForAssertion(() => Assert.Equal("false", panel.Find(AnnounceSelector).GetAttribute("aria-pressed")));
    }

    [Fact]
    public async Task NextSingerAnnounced_RedrawsTheButton()
    {
        _queue.Users.Returns([new KHostUser { Id = Guid.NewGuid(), Name = "Ann" }]);
        var panel = Render<NowPlayingPanel>();

        _nextSingerCard.IsAnnounced.Returns(true);
        await Services.GetRequiredService<IMessageBroker>().PublishAsync(new NextSingerAnnounced(new NextSingerCard { Singer = "Ann" }));

        panel.WaitForAssertion(() => Assert.Equal("true", panel.Find(AnnounceSelector).GetAttribute("aria-pressed")));
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
