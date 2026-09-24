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

/// <summary>Scrolling on every render yanks the list out from under a host scrolling it by hand;
/// only an actual selection change should ask the DOM to scroll.</summary>
public class SingerQueuePanelScrollTests : BunitContext
{
    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly KHostUser _first = new() { Id = Guid.NewGuid(), Name = "Ann" };

    public SingerQueuePanelScrollTests()
    {
        _queue.Users.Returns(_ => [_first]);
        _queue.SelectedUserId.Returns(_ => _first.Id);
        _permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

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
    public async Task UnchangedSelection_IsNotScrolledToAgainOnRerender()
    {
        var cut = Render<SingerQueuePanel>();

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("scrollIntoViewSmooth"));
        var scrollCallsAfterFirstRender = JSInterop.Invocations.Count(i => i.Identifier == "scrollIntoViewSmooth");

        // A broker announcement re-renders the panel without touching SelectedUserId.
        var renderCountBefore = cut.RenderCount;
        await _broker.PublishAsync(new SingerQueueChanged());
        cut.WaitForState(() => cut.RenderCount > renderCountBefore);

        var scrollCallsAfterRerender = JSInterop.Invocations.Count(i => i.Identifier == "scrollIntoViewSmooth");

        Assert.Equal(scrollCallsAfterFirstRender, scrollCallsAfterRerender);
    }
}
