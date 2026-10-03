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

/// <summary>Invoked by reflection: Blazor's dispatch swallows the exception a removed catch needs.</summary>
public class MediaSearchPanelActionTests : BunitContext
{
    private readonly IMediaSearchService _search = Substitute.For<IMediaSearchService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly IFlashService _flash = Substitute.For<IFlashService>();

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
        Services.AddSingleton(_flash);
        Services.AddSingleton<IControlState>(new ControlState());

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());
        Services.AddSingleton(appSettings);
        Services.AddSingleton(Substitute.For<ICacheService>());
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

    /// <summary>The YouTube download rethrows after failing its import; escaping the click ends the circuit.</summary>
    [Fact]
    public async Task Action_ThatThrows_FlashesOnceAndKeepsThePanelWithoutSearchingAgain()
    {
        var action = Action("Download", _ => throw new HttpRequestException("Permission denied (youtube.com:443)"),
            refreshesResults: true);
        var panel = await SearchedPanelAsync(action);

        await panel.Find(".kh-table__cell--actions button").ClickAsync(new());

        _flash.Received(1).Show(
            "Could not reach Provider. Check this computer is online, then try again.", FlashType.Warning);
        Assert.NotNull(panel.Find(".kh-table__cell--actions button"));
        await _search.Received(1).SearchAsync("neon moon");
    }

    /// <summary>Covers a sign-in row: leaving it up after success reads as a failed sign-in.</summary>
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
