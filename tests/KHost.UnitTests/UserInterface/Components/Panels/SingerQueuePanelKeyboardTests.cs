using KHost.Abstractions.Messaging;
using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>Catches a handler attached to nothing; advertised arrow keys that do nothing.</summary>
public class SingerQueuePanelKeyboardTests : BunitContext
{
    private const string QueueSelector = ".kh-singer-queue-panel__singer-queue";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly KHostUser _first = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly KHostUser _second = new() { Id = Guid.NewGuid(), Name = "Bob" };

    public SingerQueuePanelKeyboardTests()
    {
        this.AddKHostTestDefaults();

        _queue.Users.Returns(_ => [_first, _second]);
        _queue.SelectedUserId.Returns(_ => _second.Id);

        var venues = Substitute.For<IVenuesService>();
        venues.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(new PaginatedResult<Venue>());

        Services.AddSingleton(_queue);
        Services.AddSingleton(venues);
        var performances = Substitute.For<IPerformanceService>();
        performances.ReadQueuedAsync().Returns([]);

        Services.AddSingleton(performances);
        Services.AddSingleton(Substitute.For<IPlaybackService>());
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
}
