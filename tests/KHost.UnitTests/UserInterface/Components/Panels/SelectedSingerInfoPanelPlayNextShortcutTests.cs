using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Abstractions.Messaging;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>Global Ctrl/Cmd+Enter clicks this exact button (<c>shortcuts.js</c>); these tests cover
/// what the click does, not the document-level key matching, which is a plain JS concern.</summary>
public class SelectedSingerInfoPanelPlayNextShortcutTests : BunitContext
{
    private const string PlayNextSelector = "[data-kh-shortcut='play-next']";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly KHostUser _singer = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly Performance _first;
    private readonly Performance _second;
    private readonly Media _media;

    public SelectedSingerInfoPanelPlayNextShortcutTests()
    {
        _first = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid() };
        _second = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid() };
        _media = new Media { Id = _first.MediaId, FilePath = "/music/song.mp4", Title = "Song", Status = MediaStatus.Ready };

        _queue.SelectedUser.Returns(_singer);
        _queue.SelectedUserId.Returns(_singer.Id);
        _performances.ReadQueuedAsync().Returns(_ => [_first, _second]);
        _mediaService.ReadAsync(_first.MediaId).Returns(_ => _media);
        _mediaService.ReadAsync(_second.MediaId).Returns((Media?)null);

        _playback.HasConnectedScreenAsync().Returns(true);
        _playback.CurrentPerformance.Returns((Performance?)null);
        _playback.State.Returns(PlaybackState.Stopped);
        _playback.IsPlayingAd.Returns(false);

        JSInterop.Mode = JSRuntimeMode.Loose;

        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        var venues = Substitute.For<IVenuesService>();
        venues.ReadSelectedVenueAsync().Returns(new Venue { Id = Guid.NewGuid(), Name = "Bar" });

        Services.AddSingleton(_queue);
        Services.AddSingleton(_performances);
        Services.AddSingleton(permissions);
        Services.AddSingleton(venues);
        Services.AddSingleton(_mediaService);
        Services.AddSingleton(_playback);
        Services.AddSingleton(Substitute.For<IMediaSearchService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSingleton(Substitute.For<IUserGroupsService>());
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<ITipsService>());
        Services.AddSingleton<IMessageBroker>(_broker);
    }

    /// <summary>Only the top row carries the target: a lower one played by hand is a deliberate
    /// click, never the chord's business.</summary>
    [Fact]
    public void OnlyTheTopRow_CarriesTheShortcutTarget()
    {
        var panel = Render<SelectedSingerInfoPanel>();

        Assert.Single(panel.FindAll(PlayNextSelector));
    }

    [Fact]
    public void Clicking_LoadsAndPlaysTheTopReadyPerformance()
    {
        var panel = Render<SelectedSingerInfoPanel>();

        panel.Find(PlayNextSelector).Click();

        _playback.Received(1).LoadAsync(_first, _media);
        _playback.Received(1).PlayAsync();
    }

    /// <summary>Mirrors the console's own guard: a song already loaded disables every row's play
    /// button, the target included, so the button-native disabled attribute is what makes the
    /// hotkey a no-op.</summary>
    [Fact]
    public void WhileASongIsLoaded_TheTargetIsDisabled()
    {
        _playback.CurrentPerformance.Returns(new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id });

        var panel = Render<SelectedSingerInfoPanel>();

        // The native `disabled` attribute is what stops the click in a real browser; bunit's
        // simulated click does not enforce it (proven: LoadAndPlayAsync's own state guard only
        // fires while PlaybackState.Playing, so a click still reaches it here), so the attribute
        // is the thing to assert, the same way every other disabled-row test in this file's
        // neighbours does.
        Assert.True(panel.Find(PlayNextSelector).HasAttribute("disabled"));
    }

    /// <summary>The top song still downloading must not skip ahead to a later ready one: "next"
    /// means the top of the queue, not the first playable row.</summary>
    [Fact]
    public void WhileTheTopSongIsNotReady_TheTargetIsDisabled()
    {
        _media.Status = MediaStatus.Downloading;

        var panel = Render<SelectedSingerInfoPanel>();

        Assert.True(panel.Find(PlayNextSelector).HasAttribute("disabled"));
    }
}
