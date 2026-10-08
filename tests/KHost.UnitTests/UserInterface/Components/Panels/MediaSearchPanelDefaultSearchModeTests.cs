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

/// <summary>App Settings' "Default search mode" (TODO 67): a fixed mode, or "Remember" reading and
/// writing a per-machine cache entry.</summary>
public class MediaSearchPanelDefaultSearchModeTests : BunitContext
{
    private const string PrimarySelector = ".kh-split-btn__primary";
    private const string ToggleSelector = ".kh-split-btn__toggle";
    private const string MenuSelector = ".kh-split-btn__menu button";

    private const string LocalSource = nameof(LocalMediaProvider);
    private const string ExampleSource = "ExampleMediaProvider";
    private const string CacheKey = "search-mode-last-used";

    private readonly IMediaSearchService _search = Substitute.For<IMediaSearchService>();
    private readonly IAppSettingsService _appSettings = Substitute.For<IAppSettingsService>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly ControlState _controlState = new();

    public MediaSearchPanelDefaultSearchModeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var local = Substitute.For<IMediaProvider>();
        local.SourceName.Returns(LocalSource);
        local.DisplayName.Returns("Local");

        var example = Substitute.For<IMediaProvider>();
        example.SourceName.Returns(ExampleSource);
        example.DisplayName.Returns("Example");

        _search.Providers.Returns([local, example]);
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
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton<IControlState>(_controlState);
        Services.AddSingleton(_appSettings);
        Services.AddSingleton(_cache);
    }

    [Fact]
    public void FixedDefault_StartsThePanelInTheConfiguredMode()
    {
        _appSettings.Current.Returns(new AppSettings { DefaultSearchMode = ExampleSource });

        var panel = Render<MediaSearchPanel>();

        Assert.Equal("Example", panel.Find(PrimarySelector).TextContent.Trim());
    }

    /// <summary>A plugin that was uninstalled since the mode was configured must not leave a dead
    /// button; the panel's own Local fallback already covers this.</summary>
    [Fact]
    public void FixedDefault_NamingAProviderNoLongerLoaded_FallsBackToLocal()
    {
        _appSettings.Current.Returns(new AppSettings { DefaultSearchMode = "AnUninstalledPlugin" });

        var panel = Render<MediaSearchPanel>();

        Assert.Equal("Local", panel.Find(PrimarySelector).TextContent.Trim());

        panel.Find(PrimarySelector).Click();

        _search.Received(1).SearchAsync(Arg.Any<string>(), LocalSource);
    }

    [Fact]
    public void Remember_WithNothingCachedYet_StartsInLocal()
    {
        _appSettings.Current.Returns(new AppSettings { DefaultSearchMode = AppSettings.RememberLastSearchMode });
        _cache.LoadAsync<string>(CacheKey).Returns((string?)null);

        var panel = Render<MediaSearchPanel>();

        Assert.Equal("Local", panel.Find(PrimarySelector).TextContent.Trim());
    }

    [Fact]
    public void Remember_WithAPreviousPickCached_StartsThere()
    {
        _appSettings.Current.Returns(new AppSettings { DefaultSearchMode = AppSettings.RememberLastSearchMode });
        _cache.LoadAsync<string>(CacheKey).Returns(ExampleSource);

        var panel = Render<MediaSearchPanel>();

        Assert.Equal("Example", panel.Find(PrimarySelector).TextContent.Trim());
    }

    [Fact]
    public void Remember_PickingAMode_SavesItToTheCache()
    {
        _appSettings.Current.Returns(new AppSettings { DefaultSearchMode = AppSettings.RememberLastSearchMode });
        _cache.LoadAsync<string>(CacheKey).Returns((string?)null);

        var panel = Render<MediaSearchPanel>();
        panel.Find(ToggleSelector).Click();
        panel.Find(MenuSelector).Click();

        _cache.Received(1).SaveAsync(CacheKey, ExampleSource);
    }

    /// <summary>The whole point of "Remember": the next render starts where the last pick left off.
    /// The cache is the one thing a restart does not clear, unlike <see cref="IControlState"/>.</summary>
    [Fact]
    public void Remember_APickSaved_IsWhatTheNextRenderStartsIn()
    {
        _appSettings.Current.Returns(new AppSettings { DefaultSearchMode = AppSettings.RememberLastSearchMode });
        _cache.LoadAsync<string>(CacheKey).Returns((string?)null);

        var first = Render<MediaSearchPanel>();
        first.Find(ToggleSelector).Click();
        first.Find(MenuSelector).Click();

        _cache.Received(1).SaveAsync(CacheKey, ExampleSource);

        // What a fresh circuit's ControlState starts as; the save above is what it reads back.
        _controlState.MediaSearchSource = null;
        _cache.LoadAsync<string>(CacheKey).Returns(ExampleSource);

        var rebuilt = Render<MediaSearchPanel>();

        Assert.Equal("Example", rebuilt.Find(PrimarySelector).TextContent.Trim());
    }

    /// <summary>With a fixed default, a pick is for this session only — never written to the cache.</summary>
    [Fact]
    public void FixedDefault_PickingAMode_DoesNotWriteTheCache()
    {
        _appSettings.Current.Returns(new AppSettings { DefaultSearchMode = LocalSource });

        var panel = Render<MediaSearchPanel>();
        panel.Find(ToggleSelector).Click();
        panel.Find(MenuSelector).Click();

        _cache.DidNotReceive().SaveAsync(Arg.Any<string>(), Arg.Any<string>());
    }
}
