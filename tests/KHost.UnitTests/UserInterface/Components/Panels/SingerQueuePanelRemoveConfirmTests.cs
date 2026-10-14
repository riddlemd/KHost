using Microsoft.Extensions.Logging.Abstractions;
using KHost.Abstractions.Messaging;
using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>A venue's "prompt before removing" setting is cached from RefreshVenueSettingsAsync, so
/// a click either confirms first or removes straight away — never both, never a read in between.</summary>
public class SingerQueuePanelRemoveConfirmTests : BunitContext
{
    private const string RemoveButtonSelector = ".kh-singer-queue-panel__singer-queue__singer__remove-btn";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    // A substitute IDialogService never runs ConfirmIfAsync's own ask/else branch — only the real
    // implementation does — so this needs the genuine service, same as DialogHostSaveTests.
    private readonly DialogService _dialogs = new(NullLogger<DialogService>.Instance, Substitute.For<IDisplaySwitcher>());
    private readonly KHostUser _singer = new() { Id = Guid.NewGuid(), Name = "Ann" };

    public SingerQueuePanelRemoveConfirmTests()
    {
        this.AddKHostTestDefaults();

        _queue.Users.Returns(_ => [_singer]);
        _venues.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(new PaginatedResult<Venue>());

        var performances = Substitute.For<IPerformanceService>();
        performances.ReadQueuedAsync().Returns([]);

        Services.AddSingleton(_queue);
        Services.AddSingleton(_venues);
        Services.AddSingleton(performances);
        Services.AddSingleton(Substitute.For<IPlaybackService>());
        Services.AddSingleton<IDialogService>(_dialogs);
    }

    [Fact]
    public void PromptOff_RemovesWithNoDialog()
    {
        _venues.ReadSelectedVenueAsync().Returns(new Venue
        { Id = Guid.NewGuid(), Name = "Bar", Settings = new Venue.VenueSettings { PromptBeforeRemovingSinger = false } });

        var dialogShown = false;
        _dialogs.ShowRequested += (_, _) => dialogShown = true;

        var cut = Render<SingerQueuePanel>();
        cut.Find(RemoveButtonSelector).Click();

        _queue.Received(1).RemoveUserAsync(_singer.Id);
        Assert.False(dialogShown);
    }

    [Fact]
    public void PromptOn_AsksBeforeRemoving()
    {
        _venues.ReadSelectedVenueAsync().Returns(new Venue
        { Id = Guid.NewGuid(), Name = "Bar", Settings = new Venue.VenueSettings { PromptBeforeRemovingSinger = true } });

        var dialogShown = false;
        _dialogs.ShowRequested += (_, _) => dialogShown = true;

        var cut = Render<SingerQueuePanel>();
        cut.Find(RemoveButtonSelector).Click();

        Assert.True(dialogShown);
        _queue.DidNotReceive().RemoveUserAsync(Arg.Any<Guid>());
    }
}
