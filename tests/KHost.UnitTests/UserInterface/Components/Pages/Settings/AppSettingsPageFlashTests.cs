using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

/// <summary>A save's result used to render as a banner at the top of the page, invisible once the
/// host had scrolled down; it now goes through the shared notice host instead.</summary>
public class AppSettingsPageFlashTests : BunitContext
{
    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();
    private readonly IFlashService _flash = Substitute.For<IFlashService>();
    private readonly AppSettings _stored = new();

    public AppSettingsPageFlashTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _settings.Current.Returns(_ => _stored with { });
        _settings.DefaultMediaDirectory.Returns("/karaoke");

        Services.AddSingleton(_settings);
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(_flash);
        Services.AddSingleton(Substitute.For<IUsersService>());
    }

    [Fact]
    public async Task Saving_RaisesASuccessNotice_RatherThanRenderingAnInPageBanner()
    {
        _settings.SaveAsync(Arg.Any<AppSettings>()).Returns(new AppSettingsSaveResult(true));

        var page = Render<AppSettingsPage>();

        await page.Find(".kh-app-settings__actions button").ClickAsync(new());

        _flash.Received(1).Show("App settings saved.", FlashType.Success);
        Assert.Empty(page.FindAll(".kh-alert--warning"));
    }

    [Fact]
    public async Task ARefusedSave_RaisesAWarningNotice_RatherThanRenderingAnInPageBanner()
    {
        _settings.SaveAsync(Arg.Any<AppSettings>())
            .Returns(new AppSettingsSaveResult(false, "No admin user has a password yet"));

        var page = Render<AppSettingsPage>();

        await page.Find(".kh-app-settings__actions button").ClickAsync(new());

        _flash.Received(1).Show("No admin user has a password yet", FlashType.Warning);
        // Every kh-alert--warning left on the page is the persistent restart notice, never the
        // refusal reason: that must not print twice, once as a notice and once in the flow.
        Assert.DoesNotContain(
            page.FindAll(".kh-alert--warning"),
            el => el.TextContent.Contains("No admin user"));
    }
}
