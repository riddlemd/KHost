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

/// <summary>A song load announces the queue, the performances and playback together; the counts
/// are read at most twice and the newest answer is the one left on screen.</summary>
public class SingerQueuePanelRefreshTests : BunitContext
{
    private const string RemoveSelector = ".kh-singer-queue-panel__singer-queue__singer__remove-btn";
    private const string InfoSelector = ".kh-singer-queue-panel__singer-queue__singer__info";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly Venue _venue = new() { Id = Guid.NewGuid(), Name = "Bar" };
    private readonly KHostUser _ann = new() { Id = Guid.NewGuid(), Name = "Ann" };

    public SingerQueuePanelRefreshTests()
    {
        this.AddKHostTestDefaults(broker: _broker, dialogs: _dialogs);

        _queue.Users.Returns(_ => [_ann]);
        _performances.ReadQueuedAsync().Returns(_ => [Turn()]);
        _venues.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(new PaginatedResult<Venue>());
        _venue.Settings.PromptBeforeRemovingSinger = false;
        _venues.ReadSelectedVenueAsync().Returns(_ => _venue);

        Services.AddSingleton(_queue);
        Services.AddSingleton(_venues);
        Services.AddSingleton(_performances);
        Services.AddSingleton(Substitute.For<IPlaybackService>());
    }

    [Fact]
    public async Task ASongLoadsBurst_ReadsTheCountsTwiceNotThrice_AndEndsOnTheNewest()
    {
        var panel = Render<SingerQueuePanel>();
        panel.WaitForAssertion(() => Assert.Contains("1 song queued", panel.Find(InfoSelector).TextContent));

        var stale = new TaskCompletionSource<List<Performance>>();
        var fresh = new TaskCompletionSource<List<Performance>>();
        var third = new TaskCompletionSource<List<Performance>>();
        _performances.ClearReceivedCalls();
        _performances.ReadQueuedAsync().Returns(stale.Task, fresh.Task, third.Task);

        await _broker.PublishAsync(new SingerQueueChanged());
        await _broker.PublishAsync(new PerformancesChanged());
        await _broker.PublishAsync(new PlaybackChanged());

        await panel.InvokeAsync(() =>
        {
            fresh.SetResult([Turn(), Turn()]);
            third.SetResult([Turn(), Turn()]);
        });
        await panel.InvokeAsync(() => stale.SetResult([Turn()]));

        panel.WaitForAssertion(() => Assert.Contains("2 songs queued", panel.Find(InfoSelector).TextContent));
        Assert.Equal(2, _performances.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IPerformanceService.ReadQueuedAsync)));
    }

    /// <summary>Switching venue announces only SelectedVenueChanged; the remove prompt is that venue's.</summary>
    [Fact]
    public async Task SelectedVenueChanged_TakesTheNewVenuesRemovePrompt_WithoutRereadingTheCounts()
    {
        var panel = Render<SingerQueuePanel>();
        panel.WaitForAssertion(() => Assert.Contains("1 song queued", panel.Find(InfoSelector).TextContent));
        panel.Find(RemoveSelector).Click();
        await _dialogs.Received(1).ConfirmIfAsync(false, Arg.Any<Func<Task>>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action?>());
        _performances.ClearReceivedCalls();

        _venue.Settings.PromptBeforeRemovingSinger = true;
        await _broker.PublishAsync(new SelectedVenueChanged());
        panel.Find(RemoveSelector).Click();

        await _dialogs.Received(1).ConfirmIfAsync(true, Arg.Any<Func<Task>>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action?>());
        await _performances.DidNotReceive().ReadQueuedAsync();
    }

    [Fact]
    public async Task PlaybackChanged_RereadsNothing()
    {
        var panel = Render<SingerQueuePanel>();
        panel.WaitForAssertion(() => Assert.Contains("1 song queued", panel.Find(InfoSelector).TextContent));
        _performances.ClearReceivedCalls();
        _venues.ClearReceivedCalls();

        await _broker.PublishAsync(new PlaybackChanged());

        await _performances.DidNotReceive().ReadQueuedAsync();
        await _venues.DidNotReceive().ReadSelectedVenueAsync();
    }

    private Performance Turn() => new() { Id = Guid.NewGuid(), SingerId = _ann.Id, MediaId = Guid.NewGuid() };
}
