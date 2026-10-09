using Bunit;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

public class AppSettingsPageAdDurationTests : BunitContext
{
    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();
    private readonly AppSettings _stored = new() { AdDefaultDurationSeconds = 15 };

    public AppSettingsPageAdDurationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddNewVenuesSection();

        _settings.Current.Returns(_ => _stored with { });
        _settings.DefaultMediaDirectory.Returns("/karaoke");
        _settings.SaveAsync(Arg.Any<AppSettings>()).Returns(new AppSettingsSaveResult(true));

        Services.AddSingleton(_settings);
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddFFmpegSection();
        Services.AddBreakMusicSection();
        Services.AddSearchSection();
    }

    [Fact]
    public void AdDuration_OffersFiveToThirtySeconds_WithTheStoredOneChosen()
    {
        var page = Render<AppSettingsPage>();

        var options = page.FindAll("select#ad-default-duration option");

        Assert.Equal(["5", "10", "15", "20", "25", "30"], options.Select(o => o.GetAttribute("value")));
        Assert.Equal("5 seconds", options[0].TextContent);
        Assert.Equal("15", page.Find("select#ad-default-duration").GetAttribute("value"));
        Assert.DoesNotContain("(seconds)", page.Find("select#ad-default-duration").Closest(".kh-app-settings__row")!.TextContent);
    }

    [Fact]
    public async Task AdDuration_AChoiceMade_IsWhatSaveWrites()
    {
        var page = Render<AppSettingsPage>();

        page.Find("select#ad-default-duration").Change("25");
        await page.Find(".kh-app-settings__actions button").ClickAsync(new());

        await _settings.Received(1).SaveAsync(Arg.Is<AppSettings>(s => s.AdDefaultDurationSeconds == 25));
    }
}
