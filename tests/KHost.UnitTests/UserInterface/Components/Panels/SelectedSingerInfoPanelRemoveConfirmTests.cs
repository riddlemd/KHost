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

/// <summary>A venue's "prompt before removing" setting is cached from RefreshVenueSettingsAsync, so
/// a click either confirms first or removes straight away — never both, never a read in between.</summary>
public class SelectedSingerInfoPanelRemoveConfirmTests : BunitContext
{
    private const string RemoveButtonSelector = ".kh-selected-singer-info-panel__remove-btn";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    // A substitute IDialogService never runs ConfirmIfAsync's own ask/else branch — only the real
    // implementation does — so this needs the genuine service, same as DialogHostSaveTests.
    private readonly DialogService _dialogs = new(NullLogger<DialogService>.Instance, []);
    private readonly KHostUser _singer = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly Performance _performance;
    private readonly Media _media;

    public SelectedSingerInfoPanelRemoveConfirmTests()
    {
        _performance = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid() };
        _media = new Media { Id = _performance.MediaId, FilePath = "/music/song.mp4", Title = "Song", Status = MediaStatus.Ready };

        _queue.SelectedUser.Returns(_singer);
        _queue.SelectedUserId.Returns(_singer.Id);
        _performances.ReadQueuedAsync().Returns(_ => [_performance]);

        JSInterop.Mode = JSRuntimeMode.Loose;

        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        var mediaService = Substitute.For<IMediaService>();
        mediaService.ReadAsync(_media.Id).Returns(_media);

        Services.AddSingleton(_queue);
        Services.AddSingleton(_performances);
        Services.AddSingleton(permissions);
        Services.AddSingleton(_venues);
        Services.AddSingleton(mediaService);
        Services.AddSingleton(Substitute.For<IPlaybackService>());
        Services.AddSingleton(Substitute.For<IMediaSearchService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSingleton(Substitute.For<IUserGroupsService>());
        Services.AddSingleton<IDialogService>(_dialogs);
        Services.AddSingleton(Substitute.For<ITipsService>());
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    [Fact]
    public void PromptOff_RemovesWithNoDialog()
    {
        _venues.ReadSelectedVenueAsync().Returns(new Venue
        { Id = Guid.NewGuid(), Name = "Bar", Settings = new Venue.VenueSettings { PromptBeforeRemovingPerformance = false } });

        var dialogShown = false;
        _dialogs.ShowRequested += (_, _) => dialogShown = true;

        var cut = Render<SelectedSingerInfoPanel>();
        cut.Find(RemoveButtonSelector).Click();

        _performances.Received(1).DeleteAsync(_performance.Id);
        Assert.False(dialogShown);
    }

    [Fact]
    public void PromptOn_AsksBeforeRemoving()
    {
        _venues.ReadSelectedVenueAsync().Returns(new Venue
        { Id = Guid.NewGuid(), Name = "Bar", Settings = new Venue.VenueSettings { PromptBeforeRemovingPerformance = true } });

        var dialogShown = false;
        _dialogs.ShowRequested += (_, _) => dialogShown = true;

        var cut = Render<SelectedSingerInfoPanel>();
        cut.Find(RemoveButtonSelector).Click();

        Assert.True(dialogShown);
        _performances.DidNotReceive().DeleteAsync(Arg.Any<Guid>());
    }
}
