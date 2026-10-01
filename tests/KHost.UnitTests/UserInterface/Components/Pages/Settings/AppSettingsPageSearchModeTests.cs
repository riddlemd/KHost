using Bunit;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

public class AppSettingsPageSearchModeTests : BunitContext
{
    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();
    private readonly AppSettings _stored = new() { DefaultSearchMode = AppSettings.LocalSearchMode };

    public AppSettingsPageSearchModeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _settings.Current.Returns(_ => _stored with { });
        _settings.DefaultMediaDirectory.Returns("/karaoke");
        _settings.SaveAsync(Arg.Any<AppSettings>()).Returns(new AppSettingsSaveResult(true));

        Services.AddSingleton(_settings);
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddFFmpegSection();
        Services.AddSearchSection(
            AppSettingsPageServices.FakeLocalProvider(),
            AppSettingsPageServices.FakeProvider("KaraFunMediaProvider", "KaraFun"));
    }

    [Fact]
    public void DefaultSearchMode_ListsRememberThenEveryLoadedMode_WithTheStoredOneChosen()
    {
        var page = Render<AppSettingsPage>();

        var options = page.FindAll("select#default-search-mode option");

        Assert.Equal(
            [AppSettings.RememberLastSearchMode, AppSettings.LocalSearchMode, "KaraFunMediaProvider"],
            options.Select(o => o.GetAttribute("value")));
        Assert.Equal(
            ["Remember the last one used", "Local", "KaraFun"],
            options.Select(o => o.TextContent.Trim()));
        Assert.Equal(AppSettings.LocalSearchMode, page.Find("select#default-search-mode").GetAttribute("value"));
    }

    [Fact]
    public async Task DefaultSearchMode_AChoiceMade_IsWhatSaveWrites()
    {
        var page = Render<AppSettingsPage>();

        page.Find("select#default-search-mode").Change("KaraFunMediaProvider");
        await page.Find(".kh-app-settings__actions button").ClickAsync(new());

        await _settings.Received(1).SaveAsync(Arg.Is<AppSettings>(s => s.DefaultSearchMode == "KaraFunMediaProvider"));
    }

    [Fact]
    public void DefaultSearchMode_RememberOption_IsOfferedFirst()
    {
        var page = Render<AppSettingsPage>();

        var first = page.FindAll("select#default-search-mode option").First();

        Assert.Equal(AppSettings.RememberLastSearchMode, first.GetAttribute("value"));
        Assert.Equal("Remember the last one used", first.TextContent.Trim());
    }
}
