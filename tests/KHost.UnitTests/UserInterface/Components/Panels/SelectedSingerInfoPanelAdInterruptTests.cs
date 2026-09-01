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

/// <summary>
/// An ad plays with State Playing and no CurrentPerformance, which is the exact shape the "another
/// song is loaded" guard reads as a foreign performance. Left alone it swallowed the click on an
/// enabled-looking button, so a host could not take the room back from a fifteen-second card.
/// </summary>
public class SelectedSingerInfoPanelAdInterruptTests : BunitContext
{
    private const string PlayButtonSelector = ".kh-selected-singer-info-panel__row .kh-split-btn__primary";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly KHostUser _singer = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly Performance _performance;
    private readonly Media _media;

    public SelectedSingerInfoPanelAdInterruptTests()
    {
        _performance = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid() };
        _media = new Media { Id = _performance.MediaId, FilePath = "/music/song.mp4", Title = "Song", Status = MediaStatus.Ready };

        _queue.SelectedUser.Returns(_singer);
        _queue.SelectedUserId.Returns(_singer.Id);
        _performances.ReadQueuedAsync().Returns(_ => [_performance]);
        _mediaService.ReadAsync(_media.Id).Returns(_ => _media);

        _playback.HasConnectedScreenAsync().Returns(true);

        JSInterop.Mode = JSRuntimeMode.Loose;

        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        var venues = Substitute.For<IVenuesService>();
        venues.ReadSelectedVenueAsync().Returns(new Venue { Id = Guid.NewGuid(), Name = "Bar" });

        Services.AddSingleton(_queue);
        Services.AddSingleton<IMessageBroker>(_broker);
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
    }

    [Fact]
    public async Task ClickingPlayDuringAnAd_LoadsTheSong()
    {
        _playback.State.Returns(PlaybackState.Playing);
        _playback.IsPlayingAd.Returns(true);
        _playback.CurrentPerformance.Returns((Performance?)null);

        var panel = Render<SelectedSingerInfoPanel>();
        await panel.Find(PlayButtonSelector).ClickAsync(new());

        await _playback.Received(1).LoadAsync(_performance, _media);
        await _playback.Received(1).PlayAsync();
    }

    // The other half of the rule: a song already loaded still locks every row, so the exemption
    // above opened the door to ads only. This is the affordance a host actually sees.
    [Fact]
    public void WhileAnotherPerformanceIsLoaded_ThePlayButtonIsDisabled()
    {
        _playback.State.Returns(PlaybackState.Playing);
        _playback.IsPlayingAd.Returns(false);
        _playback.CurrentPerformance.Returns(new Performance { Id = Guid.NewGuid() });

        var panel = Render<SelectedSingerInfoPanel>();

        Assert.True(panel.Find(PlayButtonSelector).HasAttribute("disabled"));
    }

    // An ad must not lock the rows the way a song does, or the button would look dead even though
    // the click now works.
    [Fact]
    public void WhileAnAdIsPlaying_ThePlayButtonStaysEnabled()
    {
        _playback.State.Returns(PlaybackState.Playing);
        _playback.IsPlayingAd.Returns(true);
        _playback.CurrentPerformance.Returns((Performance?)null);

        var panel = Render<SelectedSingerInfoPanel>();

        Assert.False(panel.Find(PlayButtonSelector).HasAttribute("disabled"));
    }
}
