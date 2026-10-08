using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

public class AppSettingsPageFFmpegTests : BunitContext
{
    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();
    private readonly IFlashService _flash = Substitute.For<IFlashService>();

    public AppSettingsPageFFmpegTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _settings.Current.Returns(_ => new AppSettings());
        _settings.DefaultMediaDirectory.Returns("/karaoke");

        Services.AddSingleton(_settings);
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(_flash);
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSearchSection();
        Services.AddBreakMusicSection();
    }

    [Fact]
    public void StatusRow_Found_ShowsEachVersionAndWhere()
    {
        Services.AddFFmpegSection(AppSettingsPageServices.Found());

        var page = Render<AppSettingsPage>();

        var ffmpeg = page.Find("[data-tool='FFmpeg'] .kh-ffmpeg-status__detail").TextContent;
        Assert.Contains("9.0", ffmpeg);
        Assert.Contains(Path.Combine("khost", "bin", "ffmpeg"), ffmpeg);
        Assert.Contains("9.0", page.Find("[data-tool='FFprobe'] .kh-ffmpeg-status__detail").TextContent);
    }

    [Fact]
    public void StatusRow_Missing_SaysMissingForBoth()
    {
        Services.AddFFmpegSection(AppSettingsPageServices.Missing());

        var page = Render<AppSettingsPage>();

        Assert.Equal("Missing", page.Find("[data-tool='FFmpeg'] .kh-ffmpeg-status__detail").TextContent);
        Assert.Equal("Missing", page.Find("[data-tool='FFprobe'] .kh-ffmpeg-status__detail").TextContent);
    }

    [Fact]
    public async Task InstallButton_Clicked_InstallsAndSaysSo()
    {
        var ffmpeg = Services.AddFFmpegSection(AppSettingsPageServices.Missing());
        ffmpeg.InstallAsync(Arg.Any<CancellationToken>()).Returns(AppSettingsPageServices.Found() with
        {
            Install = new FFmpegInstallProgress { State = FFmpegInstallState.Succeeded },
        });

        var page = Render<AppSettingsPage>();
        var button = page.Find(".kh-app-settings__install");

        Assert.Equal("Install FFmpeg", button.TextContent.Trim());

        await button.ClickAsync(new());

        await ffmpeg.Received(1).InstallAsync(Arg.Any<CancellationToken>());
        _flash.Received(1).Show(Arg.Is<string>(text => text.StartsWith("FFmpeg installed")), FlashType.Success);
        Assert.Contains("9.0", page.Find("[data-tool='FFmpeg'] .kh-ffmpeg-status__detail").TextContent);
    }

    [Fact]
    public async Task InstallButton_InstallFails_ShowsTheReasonAndNoSuccess()
    {
        var ffmpeg = Services.AddFFmpegSection(AppSettingsPageServices.Missing());
        ffmpeg.InstallAsync(Arg.Any<CancellationToken>()).Returns(AppSettingsPageServices.Missing() with
        {
            Install = new FFmpegInstallProgress { State = FFmpegInstallState.Failed, Error = "The download does not match." },
        });

        var page = Render<AppSettingsPage>();

        await page.Find(".kh-app-settings__install").ClickAsync(new());

        Assert.Equal("The download does not match.", page.Find(".kh-ffmpeg-status__error").TextContent);
        _flash.DidNotReceive().Show(Arg.Any<string>(), FlashType.Success);
    }

    [Fact]
    public void InstallButton_AlreadyFound_OffersAReinstall()
    {
        Services.AddFFmpegSection(AppSettingsPageServices.Found());

        var page = Render<AppSettingsPage>();

        Assert.Equal("Reinstall FFmpeg", page.Find(".kh-app-settings__install").TextContent.Trim());
    }

    [Fact]
    public void InstallButton_NoBuildForThisComputer_IsNotOffered()
    {
        var ffmpeg = Services.AddFFmpegSection(AppSettingsPageServices.Missing());
        ffmpeg.CanInstall.Returns(false);

        var page = Render<AppSettingsPage>();

        Assert.Empty(page.FindAll(".kh-app-settings__install"));
        Assert.Contains("install FFmpeg yourself", page.Markup);
    }

    [Fact]
    public void InstallButton_CanInstall_NoBuildNoteIsNotShown()
    {
        Services.AddFFmpegSection(AppSettingsPageServices.Missing());

        var page = Render<AppSettingsPage>();

        Assert.DoesNotContain("install FFmpeg yourself", page.Markup);
    }

    [Fact]
    public async Task CheckAgain_Clicked_ChecksAgain()
    {
        var ffmpeg = Services.AddFFmpegSection(AppSettingsPageServices.Missing());
        ffmpeg.CheckAsync(Arg.Any<CancellationToken>()).Returns(AppSettingsPageServices.Found());

        var page = Render<AppSettingsPage>();

        await page.Find(".kh-app-settings__recheck").ClickAsync(new());

        await ffmpeg.Received(1).CheckAsync(Arg.Any<CancellationToken>());
        Assert.Contains("9.0", page.Find("[data-tool='FFmpeg'] .kh-ffmpeg-status__detail").TextContent);
    }
}
