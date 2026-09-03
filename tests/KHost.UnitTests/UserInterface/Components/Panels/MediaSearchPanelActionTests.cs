using Microsoft.Extensions.Logging.Abstractions;
using KHost.Abstractions.Messaging;
using KHost.Domain.Services.Messaging;
using System.Reflection;
using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>
/// A plugin's download action can throw OperationCanceledException — dequeuing the Downloading row
/// cancels the token the plugin was given — and that is a cancel, not a failure to surface.
/// Invoked by reflection rather than through a click: Components' event dispatch swallows the
/// exception itself, so a click-based test passes identically with the catch removed.
/// </summary>
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
        var action = Action("Download", _ => throw new OperationCanceledException());

        var task = (Task)method.Invoke(panel.Instance, [action, Entity(action)])!;

        // Throwing here is the failure mode under test.
        await task;
    }

    /// <summary>
    /// The case this exists for is a provider signing in: the row the host clicked was the
    /// provider saying it had nothing to search with, so leaving it up reads as a failed sign-in.
    /// </summary>
    [Fact]
    public async Task Action_ThatRefreshesResults_RunsTheSameSearchAgain()
    {
        var action = Action("Sign in", _ => Task.CompletedTask, refreshesResults: true);
        var panel = await SearchedPanelAsync(action);

        panel.Find(".kh-table__cell--actions button").Click();

        await _search.Received(2).SearchAsync("neon moon");
    }

    [Fact]
    public async Task Action_ThatDoesNotRefreshResults_LeavesTheResultsAlone()
    {
        var action = Action("Enqueue", _ => Task.CompletedTask);
        var panel = await SearchedPanelAsync(action);

        panel.Find(".kh-table__cell--actions button").Click();

        await _search.Received(1).SearchAsync("neon moon");
    }

    private static MediaProviderAction Action(
        string displayName, Func<MediaSearchEntity, Task> perform, bool refreshesResults = false)
        => new()
        {
            DisplayName = displayName,
            Icon = "plus-lg",
            PerformAsync = perform,
            RefreshesResults = refreshesResults,
        };

    private static MediaSearchEntity Entity(MediaProviderAction action) => new()
    {
        SourceDisplayName = "Provider",
        Source = "Remote",
        ForeignKey = Guid.NewGuid().ToString(),
        Title = "Song",
        SupportedActions = [action],
    };

    /// <summary>Renders the panel and runs a real search, so the row and its button exist to click.</summary>
    private async Task<IRenderedComponent<MediaSearchPanel>> SearchedPanelAsync(MediaProviderAction action)
    {
        _search.SearchAsync("neon moon").Returns([Entity(action)]);

        var panel = Render<MediaSearchPanel>();
        panel.Find("input[data-kh-shortcut='media-search']").Input("neon moon");
        panel.Find("input[data-kh-shortcut='media-search']").KeyDown(Key.Enter);

        panel.WaitForElement(".kh-table__cell--actions button");
        await Task.CompletedTask;

        return panel;
    }
}
