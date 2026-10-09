using Bunit;
using KHost.Abstractions.Services;
using KHost.Domain.Services.VideoEncoding;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

public class AppSettingsPageVideoEncoderTests : BunitContext
{
    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();
    private readonly AppSettings _stored = new() { VideoEncoder = VideoEncoderPreference.Software };

    public AppSettingsPageVideoEncoderTests()
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
    public void VideoEncoder_OffersEveryChoice_WithTheStoredOneChosen()
    {
        var page = Render<AppSettingsPage>();

        var options = page.FindAll("select#video-encoder option");

        Assert.Equal(["Auto", "Hardware", "Software"], options.Select(o => o.GetAttribute("value")));
        Assert.Equal(["Auto", "Hardware", "Software"], options.Select(o => o.TextContent));
        Assert.Equal("Software", page.Find("select#video-encoder").GetAttribute("value"));
    }

    [Fact]
    public async Task VideoEncoder_AChoiceMade_IsWhatSaveWrites()
    {
        var page = Render<AppSettingsPage>();

        page.Find("select#video-encoder").Change("Hardware");
        await page.Find(".kh-app-settings__actions button").ClickAsync(new());

        await _settings.Received(1).SaveAsync(Arg.Is<AppSettings>(s => s.VideoEncoder == VideoEncoderPreference.Hardware));
    }
}
