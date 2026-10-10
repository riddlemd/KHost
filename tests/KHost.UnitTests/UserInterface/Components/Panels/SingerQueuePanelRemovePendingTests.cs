using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>While a remove confirm is open the singer queue holds still: the confirm removes the
/// singer it was opened for, so a moved selection or a second confirm would point at the wrong row.</summary>
public class SingerQueuePanelRemovePendingTests : BunitContext
{
    private const string QueueSelector = ".kh-singer-queue-panel__singer-queue";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    // The real service and host, so the confirm is the one a host actually sees.
    private readonly DialogService _dialogs = new(NullLogger<DialogService>.Instance, []);
    private readonly KHostUser _first = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly KHostUser _second = new() { Id = Guid.NewGuid(), Name = "Bob <i>" };
    private int _confirmsShown;

    public SingerQueuePanelRemovePendingTests()
    {
        List<KHostUser> users = [_first, _second];
        _queue.Users.Returns(_ => users);
        _queue.SelectedUserId.Returns(_ => _second.Id);

        var venues = Substitute.For<IVenuesService>();
        venues.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(new PaginatedResult<Venue>());
        venues.ReadSelectedVenueAsync().Returns(new Venue
        { Id = Guid.NewGuid(), Name = "Bar", Settings = new Venue.VenueSettings { PromptBeforeRemovingSinger = true } });

        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        var performances = Substitute.For<IPerformanceService>();
        performances.ReadQueuedAsync().Returns([]);

        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(_queue);
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
        Services.AddSingleton(permissions);
        Services.AddSingleton(venues);
        Services.AddSingleton(performances);
        Services.AddSingleton(Substitute.For<IMediaService>());
        Services.AddSingleton(Substitute.For<IPlaybackService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSingleton<IDialogService>(_dialogs);
        Services.AddSingleton(Substitute.For<IFlashService>());

        _dialogs.ShowRequested += (_, _) => _confirmsShown++;
    }

    [Fact]
    public void Delete_ThenListKeys_WhileTheConfirmIsOpen_DoNothing()
    {
        var host = Render<DialogHost>();
        var panel = Render<SingerQueuePanel>();

        panel.Find(QueueSelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });
        host.WaitForAssertion(() => Assert.Single(host.FindAll(".kh-confirmation-dialog")));

        panel.Find(QueueSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        panel.Find(QueueSelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });

        Assert.Equal(1, _confirmsShown);
        _queue.DidNotReceive().SelectUserAsync(Arg.Any<Guid>());
    }

    [Fact]
    public void TheConfirm_NamesTheSingerAsText()
    {
        var host = Render<DialogHost>();

        Render<SingerQueuePanel>().Find(QueueSelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });

        Assert.Contains("Bob <i>", host.WaitForElement(".kh-confirmation-dialog__message").TextContent);
    }

    [Fact]
    public void Escape_CancelsTheConfirm_RemovesNobody_AndFreesTheList()
    {
        var host = Render<DialogHost>();
        var panel = Render<SingerQueuePanel>();

        panel.Find(QueueSelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });
        host.WaitForElement(".kh-confirmation-dialog").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        host.WaitForAssertion(() => Assert.Empty(host.FindAll(".kh-confirmation-dialog")));
        _queue.DidNotReceive().RemoveUserAsync(Arg.Any<Guid>());

        panel.Find(QueueSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        _queue.Received(1).SelectUserAsync(_first.Id);
    }

    [Fact]
    public void Remove_RemovesTheSingerTheConfirmWasOpenedFor()
    {
        var host = Render<DialogHost>();
        var panel = Render<SingerQueuePanel>();

        panel.Find(QueueSelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });
        host.WaitForElement(".kh-button--remove").Click();

        host.WaitForAssertion(() => _queue.Received(1).RemoveUserAsync(_second.Id));
        _queue.DidNotReceive().RemoveUserAsync(_first.Id);

        panel.Find(QueueSelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });
        Assert.Equal(2, _confirmsShown);
    }

    /// <summary>Confirmed with no close after it, so only the confirm itself can end the ask.</summary>
    [Fact]
    public async Task Confirming_WithoutAClose_FreesTheList()
    {
        ConfirmationDialog.DialogRequest? request = null;
        _dialogs.ShowRequested += (_, r) => request = r as ConfirmationDialog.DialogRequest;
        var panel = Render<SingerQueuePanel>();

        panel.Find(QueueSelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });
        await panel.InvokeAsync(() => request!.OnConfirm());
        panel.Find(QueueSelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });

        Assert.Equal(2, _confirmsShown);
    }

    [Fact]
    public void ARowRemoveButton_WhileTheConfirmIsOpen_OpensNoSecond()
    {
        var host = Render<DialogHost>();
        var panel = Render<SingerQueuePanel>();

        panel.Find(QueueSelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });
        panel.FindAll(".kh-singer-queue-panel__singer-queue__singer__remove-btn")[0].Click();

        Assert.Equal(1, _confirmsShown);
    }
}
