using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.MediaProviders;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>
/// The pick has to outlive the panel, which is rebuilt whenever the selected singer changes, so it
/// is read back off <see cref="IControlState"/> rather than off a field the rebuild would clear.
/// </summary>
public class MediaSearchPanelSourceSelectionTests : BunitContext
{
    private const string PrimarySelector = ".kh-split-btn__primary";
    private const string ToggleSelector = ".kh-split-btn__toggle";
    private const string MenuSelector = ".kh-split-btn__menu button";

    private const string ProviderSource = "KaraFun";
    private const string LocalSource = nameof(LocalMediaProvider);

    private readonly IMediaSearchService _search = Substitute.For<IMediaSearchService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly ControlState _controlState = new();

    public MediaSearchPanelSourceSelectionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Both, in the order the real console has them: the local library is a provider like any
        // other, and it is what the button falls back to.
        var local = Substitute.For<IMediaProvider>();
        local.SourceName.Returns(LocalSource);
        local.DisplayName.Returns("Local");

        var provider = Substitute.For<IMediaProvider>();
        provider.SourceName.Returns(ProviderSource);
        provider.DisplayName.Returns("KaraFun");

        _search.Providers.Returns([local, provider]);
        _search.SearchAsync(Arg.Any<string>()).Returns([]);
        _search.SearchAsync(Arg.Any<string>(), Arg.Any<string>()).Returns([]);

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
        Services.AddSingleton<IControlState>(_controlState);
    }

    [Fact]
    public void PickingAProvider_BecomesWhatThePlainButtonSearches()
    {
        var panel = Render<MediaSearchPanel>();

        panel.Find(ToggleSelector).Click();
        panel.Find(MenuSelector).Click();

        Assert.Equal(ProviderSource, _controlState.MediaSearchSource);

        panel.Find(PrimarySelector).Click();

        _search.Received(1).SearchAsync(Arg.Any<string>(), ProviderSource);
        _search.DidNotReceive().SearchAsync(Arg.Any<string>(), LocalSource);
    }

    /// <summary>
    /// A remote provider is a metered round trip, and the query is usually still half-typed when
    /// the source is picked — aiming the button must not spend a search.
    /// </summary>
    [Fact]
    public void PickingAProvider_DoesNotSearchOnItsOwn()
    {
        var panel = Render<MediaSearchPanel>();

        panel.Find(ToggleSelector).Click();
        panel.Find(MenuSelector).Click();

        _search.DidNotReceive().SearchAsync(Arg.Any<string>());
        _search.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    /// <summary>
    /// A rebuilt panel is a fresh component reading the same state — the pick has to survive that,
    /// which is the whole reason it is not a field.
    /// </summary>
    [Fact]
    public void APickMadeBeforeARebuild_StillDrivesTheButtonAfterIt()
    {
        var first = Render<MediaSearchPanel>();
        first.Find(ToggleSelector).Click();
        first.Find(MenuSelector).Click();

        var rebuilt = Render<MediaSearchPanel>();
        rebuilt.Find(PrimarySelector).Click();

        _search.Received(1).SearchAsync(Arg.Any<string>(), ProviderSource);
        _search.DidNotReceive().SearchAsync(Arg.Any<string>(), LocalSource);
    }

    /// <summary>
    /// The remembered provider can be gone by the next search — a plugin is unloaded between them —
    /// and a button that then searches nothing reads as the search being broken.
    /// </summary>
    [Fact]
    public void ARememberedProviderThatIsNoLongerRegistered_FallsBackToTheLibrary()
    {
        _controlState.MediaSearchSource = "AProviderThatWasUninstalled";

        var panel = Render<MediaSearchPanel>();

        Assert.Equal("Local", panel.Find(PrimarySelector).TextContent.Trim());

        panel.Find(PrimarySelector).Click();

        _search.Received(1).SearchAsync(Arg.Any<string>(), LocalSource);
    }

    [Fact]
    public void WithNothingPicked_ThePlainButtonSearchesTheLibrary()
    {
        var panel = Render<MediaSearchPanel>();

        panel.Find(PrimarySelector).Click();

        _search.Received(1).SearchAsync(Arg.Any<string>(), LocalSource);
    }

    [Fact]
    public void TheButton_NamesTheSourceItSearches()
    {
        var panel = Render<MediaSearchPanel>();

        Assert.Equal("Local", panel.Find(PrimarySelector).TextContent.Trim());

        panel.Find(ToggleSelector).Click();
        panel.Find(MenuSelector).Click();

        Assert.Equal("KaraFun", panel.Find(PrimarySelector).TextContent.Trim());
    }

    /// <summary>
    /// An entry that reselects the source already on the button is a second way to do what the
    /// press does — the list is meant to be what a press is not.
    /// </summary>
    [Fact]
    public void TheList_LeavesOutTheSourceOnTheButton()
    {
        var panel = Render<MediaSearchPanel>();

        panel.Find(ToggleSelector).Click();
        Assert.Equal(["KaraFun"], panel.FindAll(MenuSelector).Select(item => item.TextContent.Trim()));

        panel.Find(MenuSelector).Click();

        panel.Find(ToggleSelector).Click();
        Assert.Equal(["Local"], panel.FindAll(MenuSelector).Select(item => item.TextContent.Trim()));
    }
}
