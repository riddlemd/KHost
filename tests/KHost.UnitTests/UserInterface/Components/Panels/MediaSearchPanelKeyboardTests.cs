using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.MediaProviders;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>Down from the search box into the results, the arrows through them, Enter to queue, and
/// Alt+arrows to step the search source — each through a real key event on the element that has it.</summary>
public class MediaSearchPanelKeyboardTests : BunitContext
{
    private const string InputSelector = "input[data-kh-shortcut='media-search']";
    private const string ResultsSelector = ".kh-media-search-panel__results";
    private const string SelectedSelector = ".kh-media-search-panel__results__result--selected";
    private const string PrimarySelector = ".kh-split-btn__primary";

    private const string LocalSource = nameof(LocalMediaProvider);

    private readonly IMediaSearchService _search = Substitute.For<IMediaSearchService>();
    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly ControlState _controlState = new();
    private readonly List<string> _performed = [];
    private readonly Guid _singerId = Guid.NewGuid();

    public MediaSearchPanelKeyboardTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Built first: configuring a substitute inside another's Returns confuses NSubstitute.
        IReadOnlyList<IMediaProvider> providers =
            [Provider(LocalSource, "Local"), Provider("Alpha", "Alpha"), Provider("Beta", "Beta")];
        _search.Providers.Returns(providers);
        _search.SearchAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(_ => [Entity("One"), Entity("Two"), Entity("Three")]);

        _queue.Users.Returns(_ => []);
        _queue.SelectedUserId.Returns(_ => _singerId);
        _permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        var performances = Substitute.For<IPerformanceService>();
        performances.ReadQueuedAsync().Returns([]);

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());

        Services.AddSingleton(_search);
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
        Services.AddSingleton(_queue);
        Services.AddSingleton(_permissions);
        Services.AddSingleton(performances);
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton<IControlState>(_controlState);
        Services.AddSingleton(appSettings);
        Services.AddSingleton(Substitute.For<ICacheService>());
    }

    private static IMediaProvider Provider(string source, string name)
    {
        var provider = Substitute.For<IMediaProvider>();
        provider.SourceName.Returns(source);
        provider.DisplayName.Returns(name);
        return provider;
    }

    private MediaSearchEntity Entity(string title) => new()
    {
        SourceDisplayName = "Local",
        Source = "Remote",
        ForeignKey = Guid.NewGuid().ToString(),
        Title = title,
        SupportedActions =
        [
            new MediaProviderAction
            {
                DisplayName = "Enqueue",
                PerformAsync = entity => { _performed.Add($"enqueue:{entity.Title}"); return Task.CompletedTask; }
            },
            new MediaProviderAction
            {
                DisplayName = "Other",
                PerformAsync = entity => { _performed.Add($"other:{entity.Title}"); return Task.CompletedTask; }
            },
        ],
    };

    private IRenderedComponent<MediaSearchPanel> SearchedPanel()
    {
        var panel = Render<MediaSearchPanel>();
        panel.Find(InputSelector).Input("africa");
        panel.Find(InputSelector).KeyDown(Key.Enter);
        panel.WaitForElement(".kh-media-search-panel__results__result");

        return panel;
    }

    /// <summary>ElementReference.FocusAsync, as bunit records it: into the results, back to the box.</summary>
    private List<JSRuntimeInvocation> FocusCalls()
        => JSInterop.Invocations.Where(i => i.Identifier.EndsWith("focus", StringComparison.Ordinal)).ToList();

    private static string SelectedTitle(IRenderedComponent<MediaSearchPanel> panel)
        => panel.Find(SelectedSelector).QuerySelector("td")!.TextContent.Trim();

    [Fact]
    public void ArrowDown_InTheBox_SelectsTheFirstResult()
    {
        var panel = SearchedPanel();

        panel.Find(InputSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        Assert.Equal("One", SelectedTitle(panel));
        Assert.Single(FocusCalls());
    }

    [Fact]
    public void ArrowDown_InTheBox_WithNoResults_SelectsNothing()
    {
        var panel = Render<MediaSearchPanel>();

        panel.Find(InputSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        Assert.Empty(panel.FindAll(SelectedSelector));
        Assert.Empty(FocusCalls());
    }

    [Fact]
    public void Arrows_InTheResults_WalkTheRows()
    {
        var panel = SearchedPanel();
        panel.Find(InputSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        panel.Find(ResultsSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        panel.Find(ResultsSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal("Three", SelectedTitle(panel));

        panel.Find(ResultsSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        Assert.Equal("Two", SelectedTitle(panel));
    }

    [Fact]
    public void ArrowUp_OnTheFirstResult_LeavesTheResults()
    {
        var panel = SearchedPanel();
        panel.Find(InputSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        panel.Find(ResultsSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        Assert.Empty(panel.FindAll(SelectedSelector));
        Assert.Equal(2, FocusCalls().Count);
    }

    [Fact]
    public void Enter_InTheResults_RunsTheSelectedRowsFirstAction()
    {
        var panel = SearchedPanel();
        panel.Find(InputSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        panel.Find(ResultsSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        panel.Find(ResultsSelector).KeyDown(Key.Enter);

        Assert.Equal(["enqueue:Two"], _performed);
    }

    /// <summary>The row's button is disabled with nobody selected; Enter must not get round that.</summary>
    [Fact]
    public void Enter_WithNoSingerSelected_QueuesNothing()
    {
        _queue.SelectedUserId.Returns(_ => (Guid?)null);
        var panel = SearchedPanel();
        panel.Find(InputSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        panel.Find(ResultsSelector).KeyDown(Key.Enter);

        Assert.Empty(_performed);
    }

    [Fact]
    public void Enter_WithoutTheAddPermission_QueuesNothing()
    {
        _permissions.HasAsync(KHostPermission.AddToQueue).Returns(false);
        var panel = SearchedPanel();
        panel.Find(InputSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        panel.Find(ResultsSelector).KeyDown(Key.Enter);

        Assert.Empty(_performed);
    }

    [Fact]
    public void Enter_WithNoRowSelected_QueuesNothing()
    {
        var panel = SearchedPanel();

        panel.Find(ResultsSelector).KeyDown(Key.Enter);

        Assert.Empty(_performed);
    }

    [Fact]
    public void ANewSearch_ClearsTheSelection()
    {
        var panel = SearchedPanel();
        panel.Find(InputSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        panel.Find(InputSelector).KeyDown(Key.Enter);
        panel.WaitForElement(".kh-media-search-panel__results__result");

        Assert.Empty(panel.FindAll(SelectedSelector));
    }

    [Fact]
    public void TheResults_AreAKeyList()
    {
        var results = Render<MediaSearchPanel>().Find(ResultsSelector);

        Assert.True(results.HasAttribute("data-kh-keylist"));
        Assert.Equal("0", results.GetAttribute("tabindex"));
    }

    /// <summary>browser-keys.js cancels back/forward for Alt+arrows only where this attribute is.</summary>
    [Fact]
    public void TheBox_ClaimsAltArrows()
        => Assert.True(Render<MediaSearchPanel>().Find(InputSelector).HasAttribute("data-kh-alt-arrows"));

    [Fact]
    public void AltRight_StepsToTheNextSource()
    {
        var panel = Render<MediaSearchPanel>();

        panel.Find(InputSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowRight", AltKey = true });

        Assert.Equal("Alpha", _controlState.MediaSearchSource);
        Assert.Equal("Alpha", panel.Find(PrimarySelector).TextContent.Trim());
    }

    [Fact]
    public void AltLeft_FromTheFirstSource_WrapsToTheLast()
    {
        var panel = Render<MediaSearchPanel>();

        panel.Find(InputSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowLeft", AltKey = true });

        Assert.Equal("Beta", _controlState.MediaSearchSource);
    }

    [Fact]
    public void AltRight_FromTheLastSource_WrapsToTheFirst()
    {
        _controlState.MediaSearchSource = "Beta";
        var panel = Render<MediaSearchPanel>();

        panel.Find(InputSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowRight", AltKey = true });

        Assert.Equal(LocalSource, _controlState.MediaSearchSource);
    }

    /// <summary>Same as picking from the button's list: a remote provider is a metered call.</summary>
    [Fact]
    public void AltArrow_DoesNotSearchOnItsOwn()
    {
        var panel = Render<MediaSearchPanel>();

        panel.Find(InputSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowRight", AltKey = true });

        _search.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public void PlainArrowRight_LeavesTheSourceAlone()
    {
        var panel = Render<MediaSearchPanel>();
        var before = _controlState.MediaSearchSource;

        panel.Find(InputSelector).KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        Assert.Equal(before, _controlState.MediaSearchSource);
        Assert.Equal("Local", panel.Find(PrimarySelector).TextContent.Trim());
    }
}
