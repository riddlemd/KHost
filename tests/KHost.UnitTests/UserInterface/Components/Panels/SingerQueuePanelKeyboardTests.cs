using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>
/// The arrow-key rules themselves live in ListKeyboardShortcutsTests. These exist because the
/// handler was written but never attached to an element, so the shortcuts the buttons advertise
/// did nothing — only rendering the panel and pressing a key catches that.
/// </summary>
public class SingerQueuePanelKeyboardTests : BunitContext
{
    private const string QueueSelector = ".kh-singer-queue-panel__singer-queue";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly KHostUser _first = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly KHostUser _second = new() { Id = Guid.NewGuid(), Name = "Bob" };

    public SingerQueuePanelKeyboardTests()
    {
        _queue.Users.Returns(_ => [_first, _second]);
        _queue.SelectedUserId.Returns(_ => _second.Id);
        _permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        // The panel and its combo box both call into JS on first render; none of it matters here.
        JSInterop.Mode = JSRuntimeMode.Loose;

        var venues = Substitute.For<IVenuesService>();
        venues.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(new PaginatedResult<Venue>());

        Services.AddSingleton(_queue);
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
    public void ArrowDown_SelectsTheNextSinger()
    {
        _queue.SelectedUserId.Returns(_ => _first.Id);

        Render<SingerQueuePanel>().Find(QueueSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        _queue.Received(1).SelectUserAsync(_second.Id);
    }

    [Fact]
    public void ArrowUp_SelectsThePreviousSinger()
    {
        Render<SingerQueuePanel>().Find(QueueSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        _queue.Received(1).SelectUserAsync(_first.Id);
    }

    [Fact]
    public void ShiftArrowUp_MovesTheSelectedSingerUp()
    {
        Render<SingerQueuePanel>().Find(QueueSelector)
            .KeyDown(new KeyboardEventArgs { Key = "ArrowUp", ShiftKey = true });

        _queue.Received(1).MoveUserUpAsync(_second.Id);
        _queue.DidNotReceive().SelectUserAsync(Arg.Any<Guid?>());
    }

    [Fact]
    public void ShiftArrowDown_MovesTheSelectedSingerDown()
    {
        _queue.SelectedUserId.Returns(_ => _first.Id);

        Render<SingerQueuePanel>().Find(QueueSelector)
            .KeyDown(new KeyboardEventArgs { Key = "ArrowDown", ShiftKey = true });

        _queue.Received(1).MoveUserDownAsync(_first.Id);
    }

    [Fact]
    public void ShiftArrow_DoesNotReorder_WithoutTheReorderPermission()
    {
        _permissions.HasAsync(KHostPermission.ReorderQueue).Returns(false);

        Render<SingerQueuePanel>().Find(QueueSelector)
            .KeyDown(new KeyboardEventArgs { Key = "ArrowUp", ShiftKey = true });

        _queue.DidNotReceive().MoveUserUpAsync(Arg.Any<Guid>());
    }
}
