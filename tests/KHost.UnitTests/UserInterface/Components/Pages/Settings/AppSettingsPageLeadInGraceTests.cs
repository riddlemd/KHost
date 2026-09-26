using Bunit;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

public class AppSettingsPageLeadInGraceTests : BunitContext
{
    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();
    private readonly AppSettings _stored = new() { LeadInGraceSeconds = 5 };

    public AppSettingsPageLeadInGraceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _settings.Current.Returns(_ => _stored with { });
        _settings.DefaultMediaDirectory.Returns("/karaoke");
        _settings.SaveAsync(Arg.Any<AppSettings>()).Returns(new AppSettingsSaveResult(true));

        Services.AddSingleton(_settings);
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
    }

    [Fact]
    public void LeadInGrace_OffersOffFiveAndTen_WithTheStoredOneChosen()
    {
        var page = Render<AppSettingsPage>();

        var options = page.FindAll("select#lead-in-grace option");

        Assert.Equal(["0", "5", "10"], options.Select(o => o.GetAttribute("value")));
        Assert.Equal(["Off", "5 seconds", "10 seconds"], options.Select(o => o.TextContent.Trim()));
        Assert.Equal("5", page.Find("select#lead-in-grace").GetAttribute("value"));
    }

    [Fact]
    public async Task LeadInGrace_AChoiceMade_IsWhatSaveWrites()
    {
        var page = Render<AppSettingsPage>();

        page.Find("select#lead-in-grace").Change("10");
        await page.Find(".kh-app-settings__actions button").ClickAsync(new());

        await _settings.Received(1).SaveAsync(Arg.Is<AppSettings>(s => s.LeadInGraceSeconds == 10));
    }
}
