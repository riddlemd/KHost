using Microsoft.Extensions.Logging.Abstractions;
using KHost.Plugins.Sdk.Messaging;
using KHost.Domain.Services.Messaging;
using AngleSharp.Dom;
using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components;

/// <summary>
/// The menu item opens a dialog instead of navigating, which is a shape the menu did not have
/// before — every other entry is a route.
/// </summary>
public class SettingsButtonShortcutsTests : BunitContext
{
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();

    public SettingsButtonShortcutsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var venues = Substitute.For<IVenuesService>();
        venues.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(new PaginatedResult<Venue>());

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());

        Services.AddSingleton(_dialogs);
        Services.AddSingleton<IMessageBroker>(_broker);
        Services.AddSingleton(_permissions);
        Services.AddSingleton(venues);
        Services.AddSingleton(appSettings);
        Services.AddSingleton(Substitute.For<IThemeService>());
        Services.AddSingleton(Substitute.For<IBreakMusicService>());
    }

    private IElement OpenMenuAndFindShortcutsItem()
    {
        var menu = Render<SettingsButton>();
        menu.Find(".kh-dropdown__trigger").Click();

        return menu.FindAll(".kh-dropdown__item").Single(i => i.TextContent.Contains("Keyboard Shortcuts"));
    }

    [Fact]
    public void TheMenu_OffersTheShortcutsDialog_ToEveryone()
    {
        _permissions.IsAdminAsync().Returns(false);
        _permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(false);

        Assert.NotNull(OpenMenuAndFindShortcutsItem());
    }

    [Fact]
    public void ChoosingIt_OpensTheDialog()
    {
        OpenMenuAndFindShortcutsItem().Click();

        _dialogs.Received(1).ShowShortcutsAsync(Arg.Any<Action?>());
    }

    // Every other item is a route, and an empty one used to match the console's own path.
    [Fact]
    public void ItIsNotMarkedAsTheCurrentPage()
    {
        Assert.DoesNotContain("kh-dropdown__item--current", OpenMenuAndFindShortcutsItem().ClassName);
    }
}
