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

/// <summary>A truly-async permission check used to leave the drag handle unattached forever: the
/// old code only tried to attach on firstRender, and by then the permission was not back yet.</summary>
public class SingerQueuePanelSortableAttachTests : BunitContext
{
    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly TaskCompletionSource<bool> _reorderPermission = new();

    public SingerQueuePanelSortableAttachTests()
    {
        _queue.Users.Returns(_ => []);

        _permissions.HasAsync(KHostPermission.AddToQueue).Returns(true);
        _permissions.HasAsync(KHostPermission.RemoveFromQueue).Returns(true);
        // A genuinely pending task, not one that completes synchronously, is what exposed the bug.
        _permissions.HasAsync(KHostPermission.ReorderQueue).Returns(_reorderPermission.Task);

        JSInterop.Mode = JSRuntimeMode.Loose;

        var venues = Substitute.For<IVenuesService>();
        venues.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(new PaginatedResult<Venue>());

        Services.AddSingleton(_queue);
        Services.AddSingleton<IMessageBroker>(_broker);
        Services.AddSingleton(_permissions);
        Services.AddSingleton(venues);

        var performances = Substitute.For<IPerformanceService>();
        performances.ReadQueuedAsync().Returns([]);
        Services.AddSingleton(performances);
        Services.AddSingleton(Substitute.For<IMediaService>());
        Services.AddSingleton(Substitute.For<IPlaybackService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSingleton(Substitute.For<IDialogService>());
    }

    [Fact]
    public void ReorderPermissionArrivingAfterFirstRender_StillAttachesSortable()
    {
        var cut = Render<SingerQueuePanel>();

        JSInterop.VerifyNotInvoke("khSortable.init");

        _reorderPermission.SetResult(true);

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("khSortable.init"));
    }
}
