using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Panels;

public class SingerQueuePanelSortableAttachTests : BunitContext
{
    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();

    public SingerQueuePanelSortableAttachTests()
    {
        this.AddKHostTestDefaults();

        _queue.Users.Returns(_ => []);

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
    public void Render_AttachesSortable()
    {
        var cut = Render<SingerQueuePanel>();

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("khSortable.init"));
    }
}
