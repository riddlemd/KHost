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

/// <summary>Delete and Backspace in the focused queue go through the row button's own confirm and
/// refusals, and leave the neighbour selected; Ctrl+3 reaches the list through its shortcut hook.</summary>
public class SingerQueuePanelRemoveKeyTests : BunitContext
{
    private const string QueueSelector = ".kh-singer-queue-panel__singer-queue";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    // The real service: a substitute never runs ConfirmIfAsync's ask-or-act branch.
    private readonly DialogService _dialogs = new(NullLogger<DialogService>.Instance, []);
    private readonly KHostUser _first = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly KHostUser _second = new() { Id = Guid.NewGuid(), Name = "Bob" };
    private List<KHostUser> _users;

    public SingerQueuePanelRemoveKeyTests()
    {
        _users = [_first, _second];
        _queue.Users.Returns(_ => _users);
        _queue.SelectedUserId.Returns(_ => _first.Id);
        _venues.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(new PaginatedResult<Venue>());
        _permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);
        PromptBeforeRemoving(false);

        JSInterop.Mode = JSRuntimeMode.Loose;

        var performances = Substitute.For<IPerformanceService>();
        performances.ReadQueuedAsync().Returns([]);

        Services.AddSingleton(_queue);
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
        Services.AddSingleton(_permissions);
        Services.AddSingleton(_venues);
        Services.AddSingleton(performances);
        Services.AddSingleton(Substitute.For<IMediaService>());
        Services.AddSingleton(_playback);
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSingleton<IDialogService>(_dialogs);
    }

    private void PromptBeforeRemoving(bool prompt)
        => _venues.ReadSelectedVenueAsync().Returns(new Venue
        { Id = Guid.NewGuid(), Name = "Bar", Settings = new Venue.VenueSettings { PromptBeforeRemovingSinger = prompt } });

    [Theory]
    [InlineData("Delete")]
    [InlineData("Backspace")]
    public void Key_RemovesTheSelectedSinger(string key)
    {
        Render<SingerQueuePanel>().Find(QueueSelector).KeyDown(new KeyboardEventArgs { Key = key });

        _queue.Received(1).RemoveUserAsync(_first.Id);
    }

    [Fact]
    public void Delete_SelectsTheSingerBelowOnceRemoved()
    {
        Render<SingerQueuePanel>().Find(QueueSelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });

        _queue.Received(1).SelectUserAsync(_second.Id);
    }

    [Fact]
    public void Delete_OnTheLastSinger_SelectsTheOneAbove()
    {
        _queue.SelectedUserId.Returns(_ => _second.Id);

        Render<SingerQueuePanel>().Find(QueueSelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });

        _queue.Received(1).RemoveUserAsync(_second.Id);
        _queue.Received(1).SelectUserAsync(_first.Id);
    }

    /// <summary>Ctrl/Cmd+Backspace is the stop chord; it must not take a singer off as well.</summary>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void ModifiedBackspace_RemovesNobody(bool ctrl, bool meta, bool alt)
    {
        Render<SingerQueuePanel>().Find(QueueSelector)
            .KeyDown(new KeyboardEventArgs { Key = "Backspace", CtrlKey = ctrl, MetaKey = meta, AltKey = alt });

        _queue.DidNotReceive().RemoveUserAsync(Arg.Any<Guid>());
    }

    [Fact]
    public void Delete_WithPromptOn_AsksAndRemovesNothingYet()
    {
        PromptBeforeRemoving(true);
        var dialogShown = false;
        _dialogs.ShowRequested += (_, _) => dialogShown = true;

        Render<SingerQueuePanel>().Find(QueueSelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });

        Assert.True(dialogShown);
        _queue.DidNotReceive().RemoveUserAsync(Arg.Any<Guid>());
    }

    [Fact]
    public void Delete_RefusesTheSingerAtTheMic()
    {
        _playback.CurrentlyPerformingUserId.Returns(_first.Id);

        Render<SingerQueuePanel>().Find(QueueSelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });

        _queue.DidNotReceive().RemoveUserAsync(Arg.Any<Guid>());
    }

    [Fact]
    public void Delete_RefusesWithoutTheRemovePermission()
    {
        _permissions.HasAsync(KHostPermission.RemoveFromQueue).Returns(false);

        Render<SingerQueuePanel>().Find(QueueSelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });

        _queue.DidNotReceive().RemoveUserAsync(Arg.Any<Guid>());
    }

    [Fact]
    public void TheQueue_IsTheCtrl3TargetAndAKeyList()
    {
        var list = Render<SingerQueuePanel>().Find(QueueSelector);

        Assert.Equal("singer-queue", list.GetAttribute("data-kh-shortcut"));
        Assert.True(list.HasAttribute("data-kh-keylist"));
        Assert.Equal("0", list.GetAttribute("tabindex"));
    }

    [Fact]
    public void AnEmptyQueue_OffersNoCtrl3Target()
    {
        _users = [];

        Assert.Empty(Render<SingerQueuePanel>().FindAll("[data-kh-shortcut='singer-queue']"));
    }
}
