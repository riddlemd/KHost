using Bunit;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

public class AppSettingsPageGraphicsScaleTests : BunitContext
{
    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();
    private readonly AppSettings _stored = new() { GraphicsScaleHeight = 720 };

    public AppSettingsPageGraphicsScaleTests()
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
    public void GraphicsScale_OffersEveryHeight_WithTheStoredOneChosen()
    {
        var page = Render<AppSettingsPage>();

        var options = page.FindAll("select#graphics-scale option");

        Assert.Equal(["0", "720", "1080", "2160"], options.Select(o => o.GetAttribute("value")));
        Assert.Equal(["Off", "720p", "1080p", "4K"], options.Select(o => o.TextContent.Trim()));
        Assert.Equal("720", page.Find("select#graphics-scale").GetAttribute("value"));
    }

    [Fact]
    public async Task GraphicsScale_AChoiceMade_IsWhatSaveWrites()
    {
        var page = Render<AppSettingsPage>();

        page.Find("select#graphics-scale").Change("1080");
        await page.Find(".kh-app-settings__actions button").ClickAsync(new());

        await _settings.Received(1).SaveAsync(Arg.Is<AppSettings>(s => s.GraphicsScaleHeight == 1080));
    }
}
