using AngleSharp.Dom;
using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>Delete and Backspace in the focused song list go through the row button's own confirm
/// and refusals, and leave the neighbour selected; Ctrl+4 reaches the list through its hook.</summary>
public class SelectedSingerInfoPanelRemoveKeyTests : BunitContext
{
    private const string BodySelector = ".kh-selected-singer-info-panel__body";
    private const string SelectedRowSelector = ".kh-selected-singer-info-panel__row--selected";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    // The real service: a substitute never runs ConfirmIfAsync's ask-or-act branch.
    private readonly DialogService _dialogs = new(NullLogger<DialogService>.Instance, []);
    private readonly KHostUser _singer = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly Performance _first;
    private readonly Performance _second;
    private readonly List<Performance> _queued;

    public SelectedSingerInfoPanelRemoveKeyTests()
    {
        _first = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid() };
        _second = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid() };
        _queued = [_first, _second];

        _queue.SelectedUser.Returns(_singer);
        _queue.SelectedUserId.Returns(_singer.Id);
        _performances.ReadQueuedAsync().Returns(_ => _queued.ToList());
        _performances.When(p => p.DeleteAsync(Arg.Any<Guid>()))
            .Do(call => _queued.RemoveAll(p => p.Id == call.Arg<Guid>()));
        _permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);
        PromptBeforeRemoving(false);

        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(_queue);
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
        Services.AddSingleton(_performances);
        Services.AddSingleton(_permissions);
        Services.AddSingleton(_venues);
        Services.AddSingleton(_playback);
        Services.AddSingleton(Substitute.For<IMediaSearchService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSingleton(Substitute.For<IUserGroupsService>());
        Services.AddSingleton(Substitute.For<IMediaService>());
        Services.AddSingleton<IDialogService>(_dialogs);
        Services.AddSingleton(Substitute.For<ITipsService>());
    }

    private void PromptBeforeRemoving(bool prompt)
        => _venues.ReadSelectedVenueAsync().Returns(new Venue
        { Id = Guid.NewGuid(), Name = "Bar", Settings = new Venue.VenueSettings { PromptBeforeRemovingPerformance = prompt } });

    private (IRenderedComponent<SelectedSingerInfoPanel> Panel, IElement Body) RenderAndSelectFirstSong()
    {
        var panel = Render<SelectedSingerInfoPanel>();
        var body = panel.Find(BodySelector);
        body.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        return (panel, body);
    }

    [Theory]
    [InlineData("Delete")]
    [InlineData("Backspace")]
    public void Key_RemovesTheSelectedSong(string key)
    {
        RenderAndSelectFirstSong().Body.KeyDown(new KeyboardEventArgs { Key = key });

        _performances.Received(1).DeleteAsync(_first.Id);
    }

    [Fact]
    public void Delete_SelectsTheSongBelowOnceRemoved()
    {
        var (panel, body) = RenderAndSelectFirstSong();

        body.KeyDown(new KeyboardEventArgs { Key = "Delete" });

        panel.WaitForAssertion(() =>
            Assert.Equal(_second.Id.ToString(), panel.Find(SelectedRowSelector).GetAttribute("data-performance-id")));
    }

    [Fact]
    public void Delete_WithNothingSelected_RemovesNothing()
    {
        Render<SelectedSingerInfoPanel>().Find(BodySelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });

        _performances.DidNotReceive().DeleteAsync(Arg.Any<Guid>());
    }

    [Fact]
    public void CtrlBackspace_RemovesNothing()
    {
        RenderAndSelectFirstSong().Body.KeyDown(new KeyboardEventArgs { Key = "Backspace", CtrlKey = true });

        _performances.DidNotReceive().DeleteAsync(Arg.Any<Guid>());
    }

    [Fact]
    public void Delete_WithPromptOn_AsksAndRemovesNothingYet()
    {
        PromptBeforeRemoving(true);
        var dialogShown = false;
        _dialogs.ShowRequested += (_, _) => dialogShown = true;

        RenderAndSelectFirstSong().Body.KeyDown(new KeyboardEventArgs { Key = "Delete" });

        Assert.True(dialogShown);
        _performances.DidNotReceive().DeleteAsync(Arg.Any<Guid>());
    }

    [Fact]
    public void Delete_RefusesTheLoadedSong()
    {
        _playback.CurrentPerformance.Returns(_first);

        RenderAndSelectFirstSong().Body.KeyDown(new KeyboardEventArgs { Key = "Delete" });

        _performances.DidNotReceive().DeleteAsync(Arg.Any<Guid>());
    }

    [Fact]
    public void Delete_RefusesWithoutTheRemovePermission()
    {
        _permissions.HasAsync(KHostPermission.RemoveFromQueue).Returns(false);

        RenderAndSelectFirstSong().Body.KeyDown(new KeyboardEventArgs { Key = "Delete" });

        _performances.DidNotReceive().DeleteAsync(Arg.Any<Guid>());
    }

    [Fact]
    public void TheSongList_IsTheCtrl4TargetAndAKeyList()
    {
        var body = Render<SelectedSingerInfoPanel>().Find(BodySelector);

        Assert.Equal("singer-songs", body.GetAttribute("data-kh-shortcut"));
        Assert.True(body.HasAttribute("data-kh-keylist"));
        Assert.Equal("0", body.GetAttribute("tabindex"));
    }

    [Fact]
    public void NoSelectedSinger_OffersNoCtrl4Target()
    {
        _queue.SelectedUser.Returns((KHostUser?)null);
        _queue.SelectedUserId.Returns((Guid?)null);

        Assert.Empty(Render<SelectedSingerInfoPanel>().FindAll("[data-kh-shortcut='singer-songs']"));
    }
}
