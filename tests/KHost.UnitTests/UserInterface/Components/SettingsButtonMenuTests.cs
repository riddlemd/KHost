using AngleSharp.Dom;
using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Abstractions.Messaging;
using KHost.UserInterface.Components;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components;

/// <summary>Shortcuts moved to the header's help glyph; About did not move.</summary>
public class SettingsButtonMenuTests : BunitContext
{
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();

    public SettingsButtonMenuTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var venues = Substitute.For<IVenuesService>();
        venues.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(new PaginatedResult<Venue>());

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());

        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton<IMessageBroker>(_broker);
        Services.AddSingleton(_permissions);
        Services.AddSingleton(venues);
        Services.AddSingleton(appSettings);
        Services.AddSingleton(Substitute.For<IThemeService>());
        Services.AddSingleton(Substitute.For<IBreakMusicService>());
    }

    private IReadOnlyList<IElement> MenuItems()
    {
        var menu = Render<SettingsButton>();
        menu.Find(".kh-dropdown__trigger").Click();

        return menu.FindAll(".kh-dropdown__item");
    }

    [Fact]
    public void TheMenu_NoLongerOffersKeyboardShortcuts()
    {
        _permissions.IsAdminAsync().Returns(false);
        _permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(false);

        Assert.DoesNotContain(MenuItems(), i => i.TextContent.Contains("Keyboard Shortcuts"));
    }

    [Fact]
    public void TheMenu_StillOffersAbout()
    {
        _permissions.IsAdminAsync().Returns(false);
        _permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(false);

        Assert.Contains(MenuItems(), i => i.TextContent.Contains("About"));
    }
}
