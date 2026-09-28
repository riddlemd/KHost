using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>Unlike the singer queue's table, this one is absent until the singer has a song, so
/// the sortable has to follow the table in and out rather than attach once.</summary>
public class SelectedSingerInfoPanelSortableAttachTests : BunitContext
{
    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly KHostUser _singer = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly Performance _performance;
    private readonly Media _media;
    private List<Performance> _queued = [];

    public SelectedSingerInfoPanelSortableAttachTests()
    {
        _performance = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid() };
        _media = new Media { Id = _performance.MediaId, FilePath = "/music/song.mp4", Title = "Song", Status = MediaStatus.Ready };

        _queue.SelectedUser.Returns(_singer);
        _queue.SelectedUserId.Returns(_singer.Id);
        _performances.ReadQueuedAsync().Returns(_ => _queued);
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
    public void FirstSongArriving_AttachesSortable()
    {
        var cut = Render<SelectedSingerInfoPanel>();

        JSInterop.VerifyNotInvoke("khSortable.init");

        _queued = [_performance];
        _broker.Announce(new PerformancesChanged());

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("khSortable.init"));
    }

    [Fact]
    public void LastSongLeaving_DestroysSortable()
    {
        _queued = [_performance];
        var cut = Render<SelectedSingerInfoPanel>();

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("khSortable.init"));

        _queued = [];
        _broker.Announce(new PerformancesChanged());

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("khSortable.destroy"));
    }
}
