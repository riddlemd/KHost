using AngleSharp.Dom;
using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface;
using KHost.UserInterface.Components;
using KHost.UserInterface.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KHost.UnitTests.UserInterface.Components;

/// <summary>A headless host has no window to close, so the menu is its only way out.</summary>
public class SettingsButtonExitTests : BunitContext
{
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IHostApplicationLifetime _lifetime = Substitute.For<IHostApplicationLifetime>();
    private readonly BunitJSModuleInterop _exitModule;
    private Func<Task>? _onConfirm;

    public SettingsButtonExitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _exitModule = JSInterop.SetupModule("/js/host-exit.js");
        _exitModule.SetupVoid("showStopped").SetVoidResult();

        var venues = Substitute.For<IVenuesService>();
        venues.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(new PaginatedResult<Venue>());

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());

        _dialogs.ShowConfirmationAsync(Arg.Any<string>(), Arg.Do<Func<Task>>(confirm => _onConfirm = confirm),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action?>(), Arg.Any<Action?>())
            .Returns(true);

        Services.AddSingleton(_dialogs);
        Services.AddSingleton(_lifetime);
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton<IMessageBroker>(_broker);
        Services.AddSingleton(Substitute.For<IPermissionService>());
        Services.AddSingleton(venues);
        Services.AddSingleton(appSettings);
        Services.AddSingleton(Substitute.For<IThemeService>());
        Services.AddSingleton(Substitute.For<IBreakMusicService>());
    }

    private void RunAs(bool nativeShell)
        => Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>(Program.NativeShellKey, nativeShell.ToString())])
            .Build());

    private IRenderedComponent<SettingsButton> OpenMenu()
    {
        var menu = Render<SettingsButton>();
        menu.Find(".kh-dropdown__trigger").Click();
        return menu;
    }

    private static IElement? ExitItem(IRenderedComponent<SettingsButton> menu)
        => menu.FindAll(".kh-dropdown__item").FirstOrDefault(item => item.TextContent.Trim() == "Exit");

    [Fact]
    public void Menu_Headless_OffersExit()
    {
        RunAs(nativeShell: false);

        Assert.NotNull(ExitItem(OpenMenu()));
    }

    [Fact]
    public void Menu_NativeWindow_HasNoExit()
    {
        RunAs(nativeShell: true);

        Assert.Null(ExitItem(OpenMenu()));
    }

    [Fact]
    public void Exit_BeforeConfirming_LeavesTheHostRunning()
    {
        RunAs(nativeShell: false);

        ExitItem(OpenMenu())!.Click();

        Assert.NotNull(_onConfirm);
        _lifetime.DidNotReceive().StopApplication();
        Assert.Empty(_exitModule.Invocations);
    }

    [Fact]
    public async Task Exit_Confirmed_ShowsTheStoppedPageThenStopsTheHost()
    {
        RunAs(nativeShell: false);
        var stopped = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _lifetime.When(lifetime => lifetime.StopApplication())
            .Do(_ => stopped.TrySetResult(_exitModule.Invocations.Count(i => i.Identifier == "showStopped")));

        var menu = OpenMenu();
        ExitItem(menu)!.Click();
        await menu.InvokeAsync(_onConfirm!);

        // The page has to say so before the circuit drops, or the drop reads as a lost connection.
        Assert.Equal(1, await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
