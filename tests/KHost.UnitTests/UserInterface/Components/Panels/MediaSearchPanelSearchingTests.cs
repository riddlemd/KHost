using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Models;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>
/// A yt-dlp search runs for tens of seconds, so the wait and the way out of it are the panel's
/// behaviour, not decoration. Rendering and clicking is the only thing that catches a Cancel button
/// wired to nothing.
/// </summary>
public class MediaSearchPanelSearchingTests : BunitContext
{
    private const string SearchingSelector = ".kh-media-search-panel__searching";

    private readonly IMediaSearchService _search = Substitute.For<IMediaSearchService>();

    /// <summary>One per search, in order, so a test can land an earlier one after a later one.</summary>
    private readonly List<TaskCompletionSource<List<MediaSearchEntity>>> _pendingSearches = [];

    private TaskCompletionSource<List<MediaSearchEntity>> _pending => _pendingSearches[0];

    public MediaSearchPanelSearchingTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Never completes unless a test says so, which is what a slow provider looks like.
        _search.SearchAsync(Arg.Any<string>()).Returns(_ =>
        {
            var source = new TaskCompletionSource<List<MediaSearchEntity>>();
            _pendingSearches.Add(source);
            return source.Task;
        });

        _search.Providers.Returns([]);

        var queue = Substitute.For<ISingerQueueService>();
        queue.Users.Returns(_ => []);

        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        var performances = Substitute.For<IPerformanceService>();
        performances.ReadQueuedAsync().Returns([]);

        Services.AddSingleton(_search);
        Services.AddSingleton(queue);
        Services.AddSingleton(permissions);
        Services.AddSingleton(performances);
        Services.AddSingleton(Substitute.For<IDialogService>());
    }

    [Fact]
    public void AWaitingSearch_SaysSoWhereTheResultsWillBe()
    {
        var panel = Render<MediaSearchPanel>();

        panel.Find(".kh-split-button, button").Click();

        // Not merely a greyed-out button: the message and the way out belong where the host is
        // already looking.
        Assert.NotNull(panel.Find(SearchingSelector));
        Assert.Contains("Searching", panel.Find(SearchingSelector).TextContent);
    }

    [Fact]
    public void Cancelling_GivesThePanelBack_WithoutWaitingForTheProvider()
    {
        var panel = Render<MediaSearchPanel>();
        panel.Find(".kh-split-button, button").Click();

        panel.Find($"{SearchingSelector} button").Click();

        // The provider's task is still pending on purpose — cancelling must not depend on it.
        Assert.False(_pending.Task.IsCompleted);
        Assert.Empty(panel.FindAll(SearchingSelector));
    }

    [Fact]
    public void AProviderThatFinishesAfterCancelling_DoesNotPutResultsBack()
    {
        var panel = Render<MediaSearchPanel>();
        panel.Find(".kh-split-button, button").Click();
        panel.Find($"{SearchingSelector} button").Click();

        // The abandoned search returning late must not repaint a panel the host has moved on from.
        _pending.SetResult([new MediaSearchEntity
        {
            SourceDisplayName = "Library",
            Source = "Local",
            ForeignKey = Guid.NewGuid().ToString(),
            Title = "Too late",
        }]);

        panel.WaitForAssertion(() => Assert.Empty(panel.FindAll(SearchingSelector)));
        Assert.DoesNotContain("Too late", panel.Markup);
    }

}
