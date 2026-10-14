using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

public class SingerQueuePanelSortableAttachTests : BunitContext
{
    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public SingerQueuePanelSortableAttachTests()
    {
        _queue.Users.Returns(_ => []);

        JSInterop.Mode = JSRuntimeMode.Loose;

        var venues = Substitute.For<IVenuesService>();
        venues.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(new PaginatedResult<Venue>());

        Services.AddSingleton(_queue);
        Services.AddSingleton<IMessageBroker>(_broker);
        Services.AddSingleton(venues);

        var performances = Substitute.For<IPerformanceService>();
        performances.ReadQueuedAsync().Returns([]);
        Services.AddSingleton(performances);
        Services.AddSingleton(Substitute.For<IMediaService>());
        Services.AddSingleton(Substitute.For<IPlaybackService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<IFlashService>());
    }

    [Fact]
    public void Render_AttachesSortable()
    {
        var cut = Render<SingerQueuePanel>();

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("khSortable.init"));
    }
}
