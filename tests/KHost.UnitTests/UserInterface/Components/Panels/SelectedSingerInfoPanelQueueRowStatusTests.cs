using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>
/// A queued row's media can be mid-download, so the play affordance has to say that rather than
/// invite a click that PlaybackService is only going to refuse.
/// </summary>
public class SelectedSingerInfoPanelQueueRowStatusTests : BunitContext
{
    private const string PlayButtonSelector = ".kh-selected-singer-info-panel__row .kh-split-btn__primary";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly KHostUser _singer = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly Performance _performance;
    private readonly Media _media;

    public SelectedSingerInfoPanelQueueRowStatusTests()
    {
        _performance = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid() };
        _media = new Media { Id = _performance.MediaId, FilePath = "/music/song.mp4", Title = "Song", Status = MediaStatus.Downloading };

        _queue.SelectedUser.Returns(_singer);
        _queue.SelectedUserId.Returns(_singer.Id);
        _performances.ReadQueuedAsync().Returns(_ => [_performance]);
        // A substitute reference, not a snapshot: mutating _media.Status is visible on the next read.
        _mediaService.ReadAsync(_media.Id).Returns(_ => _media);

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
        Services.AddSingleton(Substitute.For<IPlaybackService>());
        Services.AddSingleton(Substitute.For<IMediaSearchService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSingleton(Substitute.For<IUserGroupsService>());
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<ITipsService>());
        Services.AddSingleton<IMessageBroker>(_broker);
    }

    [Fact]
    public void DownloadingMedia_ShowsSpinnerInsteadOfPlay_AndDisablesTheButton()
    {
        var panel = Render<SelectedSingerInfoPanel>();

        var button = panel.Find(PlayButtonSelector);

        Assert.NotEmpty(panel.FindAll($"{PlayButtonSelector} .kh-loader__spinner"));
        Assert.Empty(panel.FindAll($"{PlayButtonSelector} .bi-play-fill"));
        Assert.True(button.HasAttribute("disabled"));
    }

    [Fact]
    public void ReadyMedia_ShowsThePlayIcon_AndEnablesTheButton()
    {
        _media.Status = MediaStatus.Ready;

        var panel = Render<SelectedSingerInfoPanel>();

        var button = panel.Find(PlayButtonSelector);

        Assert.NotEmpty(panel.FindAll($"{PlayButtonSelector} .bi-play-fill"));
        Assert.Empty(panel.FindAll($"{PlayButtonSelector} .kh-loader__spinner"));
        Assert.False(button.HasAttribute("disabled"));
    }

    [Fact]
    public async Task MediaTurningReady_ReRendersTheRow_WhenMediaLibraryChangedIsPublished()
    {
        var panel = Render<SelectedSingerInfoPanel>();
        Assert.NotEmpty(panel.FindAll($"{PlayButtonSelector} .kh-loader__spinner"));

        _media.Status = MediaStatus.Ready;
        await _broker.PublishAsync(new MediaLibraryChanged());

        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll($"{PlayButtonSelector} .bi-play-fill")));
        Assert.Empty(panel.FindAll($"{PlayButtonSelector} .kh-loader__spinner"));
    }
}
