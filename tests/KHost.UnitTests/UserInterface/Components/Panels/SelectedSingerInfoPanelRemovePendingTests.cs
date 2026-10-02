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

/// <summary>While a remove confirm is open the song list holds still: the confirm deletes the song
/// it was opened for, so a moved highlight or a second confirm would point at the wrong row.</summary>
public class SelectedSingerInfoPanelRemovePendingTests : BunitContext
{
    private const string BodySelector = ".kh-selected-singer-info-panel__body";
    private const string SelectedRowSelector = ".kh-selected-singer-info-panel__row--selected";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    // The real service and host, so the confirm is the one a host actually sees.
    private readonly DialogService _dialogs = new(NullLogger<DialogService>.Instance, []);
    private readonly KHostUser _singer = new() { Id = Guid.NewGuid(), Name = "Ann <b>" };
    private readonly Performance _first;
    private readonly Performance _second;
    private readonly List<Performance> _queued;
    private int _confirmsShown;

    public SelectedSingerInfoPanelRemovePendingTests()
    {
        _first = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid() };
        _second = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid() };
        _queued = [_first, _second];

        _queue.SelectedUser.Returns(_singer);
        _queue.SelectedUserId.Returns(_singer.Id);
        _performances.ReadQueuedAsync().Returns(_ => _queued.ToList());
        _performances.When(p => p.DeleteAsync(Arg.Any<Guid>()))
            .Do(call => _queued.RemoveAll(p => p.Id == call.Arg<Guid>()));
        _venues.ReadSelectedVenueAsync().Returns(new Venue
        { Id = Guid.NewGuid(), Name = "Bar", Settings = new Venue.VenueSettings { PromptBeforeRemovingPerformance = true } });

        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        var media = Substitute.For<IMediaService>();
        media.ReadAsync(_first.MediaId).Returns(new Media { Id = _first.MediaId, FilePath = "/music/africa.mp4", Title = "Africa", Status = MediaStatus.Ready });
        media.ReadAsync(_second.MediaId).Returns(new Media { Id = _second.MediaId, FilePath = "/music/rosanna.mp4", Title = "Rosanna", Status = MediaStatus.Ready });

        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(_queue);
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
        Services.AddSingleton(_performances);
        Services.AddSingleton(permissions);
        Services.AddSingleton(_venues);
        Services.AddSingleton(Substitute.For<IPlaybackService>());
        Services.AddSingleton(Substitute.For<IMediaSearchService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSingleton(Substitute.For<IUserGroupsService>());
        Services.AddSingleton(media);
        Services.AddSingleton<IDialogService>(_dialogs);
        Services.AddSingleton(Substitute.For<ITipsService>());

        _dialogs.ShowRequested += (_, _) => _confirmsShown++;
    }

    private (IRenderedComponent<DialogHost> Host, IRenderedComponent<SelectedSingerInfoPanel> Panel) RenderWithTheSecondSongSelected()
    {
        var host = Render<DialogHost>();
        var panel = Render<SelectedSingerInfoPanel>();
        panel.WaitForAssertion(() => Assert.Equal(2, panel.FindAll("[data-performance-id]").Count));

        var body = panel.Find(BodySelector);
        body.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        panel.Find(BodySelector).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        return (host, panel);
    }

    private static string SelectedId(IRenderedComponent<SelectedSingerInfoPanel> panel)
        => panel.Find(SelectedRowSelector).GetAttribute("data-performance-id")!;

    [Fact]
    public void Delete_ThenListKeys_WhileTheConfirmIsOpen_DoNothing()
    {
        var (host, panel) = RenderWithTheSecondSongSelected();

        panel.Find(BodySelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });
        host.WaitForAssertion(() => Assert.Single(host.FindAll(".kh-confirmation-dialog")));

        panel.Find(BodySelector).KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        panel.Find(BodySelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });

        Assert.Equal(1, _confirmsShown);
        Assert.Single(host.FindAll(".kh-confirmation-dialog"));
        Assert.Equal(_second.Id.ToString(), SelectedId(panel));
    }

    [Fact]
    public void TheConfirm_NamesTheSongAndTheSinger()
    {
        var (host, panel) = RenderWithTheSecondSongSelected();

        panel.Find(BodySelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });

        var message = host.WaitForElement(".kh-confirmation-dialog__message");
        Assert.Contains("Rosanna", message.TextContent);
        // Encoded, not parsed: a name is whatever was typed.
        Assert.Contains("Ann <b>", message.TextContent);
    }

    [Fact]
    public void Escape_CancelsTheConfirm_RemovesNothing_AndFreesTheList()
    {
        var (host, panel) = RenderWithTheSecondSongSelected();

        panel.Find(BodySelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });
        host.WaitForElement(".kh-confirmation-dialog").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        host.WaitForAssertion(() => Assert.Empty(host.FindAll(".kh-confirmation-dialog")));
        _performances.DidNotReceive().DeleteAsync(Arg.Any<Guid>());

        panel.Find(BodySelector).KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        Assert.Equal(_first.Id.ToString(), SelectedId(panel));
    }

    [Fact]
    public void Remove_DeletesTheSongTheConfirmWasOpenedFor_AndFreesTheList()
    {
        var (host, panel) = RenderWithTheSecondSongSelected();

        panel.Find(BodySelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });
        panel.Find(BodySelector).KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        host.WaitForElement(".kh-button--remove").Click();

        host.WaitForAssertion(() => _performances.Received(1).DeleteAsync(_second.Id));
        _performances.DidNotReceive().DeleteAsync(_first.Id);

        panel.WaitForAssertion(() => Assert.Equal(_first.Id.ToString(), SelectedId(panel)));
        panel.Find(BodySelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });
        Assert.Equal(2, _confirmsShown);
    }

    /// <summary>Confirmed with no close after it, so only the confirm itself can end the ask.</summary>
    [Fact]
    public async Task Confirming_WithoutAClose_FreesTheList()
    {
        ConfirmationDialog.DialogRequest? request = null;
        _dialogs.ShowRequested += (_, r) => request = r as ConfirmationDialog.DialogRequest;
        var panel = Render<SelectedSingerInfoPanel>();
        panel.WaitForAssertion(() => Assert.Equal(2, panel.FindAll("[data-performance-id]").Count));
        panel.Find(BodySelector).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        panel.Find(BodySelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });
        await panel.InvokeAsync(() => request!.OnConfirm());
        panel.Find(BodySelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });

        Assert.Equal(2, _confirmsShown);
    }

    [Fact]
    public void ARowRemoveButton_WhileTheConfirmIsOpen_OpensNoSecond()
    {
        var (_, panel) = RenderWithTheSecondSongSelected();

        panel.Find(BodySelector).KeyDown(new KeyboardEventArgs { Key = "Delete" });
        panel.FindAll(".kh-selected-singer-info-panel__remove-btn")[0].Click();

        Assert.Equal(1, _confirmsShown);
    }
}
