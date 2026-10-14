using AngleSharp.Dom;
using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.UserInterface.Components;
using KHost.UserInterface.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KHost.UnitTests.UserInterface.Components;

/// <summary>Shortcuts moved to the header's help glyph; About did not move.</summary>
public class SettingsButtonMenuTests : BunitContext
{

    public SettingsButtonMenuTests()
    {
        this.AddKHostTestDefaults();

        var venues = Substitute.For<IVenuesService>();
        venues.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(new PaginatedResult<Venue>());

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());

        Services.AddSingleton(venues);
        Services.AddSingleton(appSettings);
        Services.AddSingleton(Substitute.For<IThemeService>());
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Services.AddSingleton(Substitute.For<IHostApplicationLifetime>());
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
        Assert.DoesNotContain(MenuItems(), i => i.TextContent.Contains("Keyboard Shortcuts"));
    }

    [Fact]
    public void TheMenu_StillOffersAbout()
    {
        Assert.Contains(MenuItems(), i => i.TextContent.Contains("About"));
    }

    /// <summary>The main page is the Hosting Dashboard, not "Controls" or "Console".</summary>
    [Fact]
    public void TheMenu_LeadsWithTheHostingDashboard()
    {
        Assert.Equal("Hosting Dashboard", MenuItems()[0].TextContent.Trim());
    }
}
