using Microsoft.Extensions.Logging.Abstractions;
using KHost.Plugins.Sdk.Messaging;
using KHost.Domain.Services.Messaging;
using System.Reflection;
using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Models;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>
/// A plugin's download action can throw OperationCanceledException — the host dequeuing the
/// Downloading row cancels the token the plugin was given. That is a cancel, not a failure, so
/// PerformActionAsync must not let it escape.
/// </summary>
/// <remarks>
/// Driven through a click, ASP.NET Core Components' own event dispatch already swallows an
/// OperationCanceledException raised from a handler — so a click-based test cannot tell whether
/// PerformActionAsync's own try/catch did anything; it would pass identically with the catch
/// removed. PerformActionAsync is invoked directly by reflection instead, bypassing that dispatch
/// so the assertion is actually about this method's own behaviour.
/// </remarks>
public class MediaSearchPanelActionTests : BunitContext
{
    private readonly IMediaSearchService _search = Substitute.For<IMediaSearchService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public MediaSearchPanelActionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _search.Providers.Returns([]);
        _search.SearchAsync(Arg.Any<string>()).Returns([]);

        var queue = Substitute.For<ISingerQueueService>();
        queue.Users.Returns(_ => []);

        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        var performances = Substitute.For<IPerformanceService>();
        performances.ReadQueuedAsync().Returns([]);

        Services.AddSingleton(_search);
        Services.AddSingleton<IMessageBroker>(_broker);
        Services.AddSingleton(queue);
        Services.AddSingleton(permissions);
        Services.AddSingleton(performances);
        Services.AddSingleton(Substitute.For<IDialogService>());
    }

    [Fact]
    public async Task PerformActionAsync_ActionThrowsOperationCanceledException_DoesNotSurfaceIt()
    {
        var panel = Render<MediaSearchPanel>();
        var method = typeof(MediaSearchPanel).GetMethod("PerformActionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var entity = new MediaSearchEntity
        {
            SourceDisplayName = "Provider",
            Source = "Remote",
            ForeignKey = Guid.NewGuid().ToString(),
            Title = "Song",
        };
        Func<MediaSearchEntity, Task> cancelledAction = _ => throw new OperationCanceledException();

        var task = (Task)method.Invoke(panel.Instance, [cancelledAction, entity])!;

        // Throwing here is the failure mode under test.
        await task;
    }
}
