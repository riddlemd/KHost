using AngleSharp.Dom;
using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>
/// Same reason as SingerQueuePanelKeyboardTests: only a rendered panel proves the handler is
/// attached to an element, which is the half that was missing.
/// </summary>
public class SelectedSingerInfoPanelKeyboardTests : BunitContext
{
    private const string BodySelector = ".kh-selected-singer-info-panel__body";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly KHostUser _singer = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly Performance _first;
    private readonly Performance _second;

    public SelectedSingerInfoPanelKeyboardTests()
    {
        _first = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid() };
        _second = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid() };

        _queue.SelectedUser.Returns(_singer);
        _queue.SelectedUserId.Returns(_singer.Id);
        _performances.ReadQueuedAsync().Returns(_ => [_first, _second]);
        _permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        JSInterop.Mode = JSRuntimeMode.Loose;

        var venues = Substitute.For<IVenuesService>();
        venues.ReadSelectedVenueAsync().Returns(new Venue { Id = Guid.NewGuid(), Name = "Bar" });

        Services.AddSingleton(_queue);
        Services.AddSingleton(_performances);
        Services.AddSingleton(_permissions);
        Services.AddSingleton(venues);
        Services.AddSingleton(Substitute.For<IPlaybackService>());
        Services.AddSingleton(Substitute.For<IMediaSearchService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSingleton(Substitute.For<IUserGroupsService>());
        Services.AddSingleton(Substitute.For<IMediaService>());
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<ITipsService>());
    }

    private IElement RenderAndSelectFirstSong()
    {
        var body = Render<SelectedSingerInfoPanel>().Find(BodySelector);

        // Nothing is selected until a row is picked; Down is how the panel's own rules get there.
        body.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        return body;
    }

    [Fact]
    public void ShiftArrowDown_MovesTheSelectedSongDown()
    {
        RenderAndSelectFirstSong().KeyDown(new KeyboardEventArgs { Key = "ArrowDown", ShiftKey = true });

        _performances.Received(1).MoveDownInQueueAsync(_singer.Id, _first.Id);
    }

    [Fact]
    public void ShiftArrowUp_MovesTheSelectedSongUp()
    {
        var body = RenderAndSelectFirstSong();
        body.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        body.KeyDown(new KeyboardEventArgs { Key = "ArrowUp", ShiftKey = true });

        _performances.Received(1).MoveUpInQueueAsync(_singer.Id, _second.Id);
    }

    [Fact]
    public void ShiftArrow_DoesNotReorder_WithoutTheReorderPermission()
    {
        _permissions.HasAsync(KHostPermission.ReorderQueue).Returns(false);

        RenderAndSelectFirstSong().KeyDown(new KeyboardEventArgs { Key = "ArrowDown", ShiftKey = true });

        _performances.DidNotReceive().MoveDownInQueueAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    [Fact]
    public void PlainArrow_MovesTheSelectionWithoutReordering()
    {
        RenderAndSelectFirstSong().KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        _performances.DidNotReceive().MoveDownInQueueAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
        _performances.DidNotReceive().MoveUpInQueueAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
    }
}
