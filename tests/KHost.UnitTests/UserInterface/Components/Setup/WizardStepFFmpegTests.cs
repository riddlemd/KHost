using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UnitTests.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Components.Setup;

namespace KHost.UnitTests.UserInterface.Components.Setup;

/// <summary>The download is a GPL-licensed third-party build, so the step waits for the host to
/// click Download before it starts.</summary>
public class WizardStepFFmpegTests : BunitContext
{
    private readonly IFFmpegService _ffmpeg;
    private int _completed;

    public WizardStepFFmpegTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ffmpeg = Services.AddFFmpegSection(AppSettingsPageServices.Missing());
    }

    [Fact]
    public void Missing_DownloadsNothingUntilClicked_ThenInstallsAndMovesOnOnceItIsIn()
    {
        _ffmpeg.CheckAsync(Arg.Any<CancellationToken>()).Returns(AppSettingsPageServices.Missing());
        _ffmpeg.InstallAsync(Arg.Any<CancellationToken>()).Returns(Installed());

        var step = RenderStep();

        var button = step.WaitForElement(".kh-wizard-ffmpeg__download");
        Assert.Equal("Download FFmpeg", button.TextContent.Trim());
        _ffmpeg.DidNotReceive().InstallAsync(Arg.Any<CancellationToken>());

        button.Click();

        step.WaitForAssertion(() => _ffmpeg.Received(1).InstallAsync(Arg.Any<CancellationToken>()));
        step.WaitForElement(".kh-wizard-ffmpeg__next").Click();

        Assert.Equal(1, _completed);
    }

    [Fact]
    public void Missing_WhileInstalling_OffersNeitherDownloadNorNext()
    {
        var pending = new TaskCompletionSource<FFmpegStatus>();
        _ffmpeg.CheckAsync(Arg.Any<CancellationToken>()).Returns(AppSettingsPageServices.Missing());
        _ffmpeg.InstallAsync(Arg.Any<CancellationToken>()).Returns(pending.Task);

        var step = RenderStep();

        step.WaitForElement(".kh-wizard-ffmpeg__download").Click();

        step.WaitForAssertion(() => _ffmpeg.Received(1).InstallAsync(Arg.Any<CancellationToken>()));
        Assert.Empty(step.FindAll(".kh-wizard-ffmpeg__download"));
        Assert.Empty(step.FindAll(".kh-wizard-ffmpeg__retry"));
        Assert.Empty(step.FindAll(".kh-wizard-ffmpeg__next"));
        Assert.Contains("Installing", step.Find(".kh-setup-wizard__actions").TextContent);
    }

    [Fact]
    public void Found_ShowsTheVersions_AndDownloadsNothing()
    {
        _ffmpeg.CheckAsync(Arg.Any<CancellationToken>()).Returns(AppSettingsPageServices.Found());

        var step = RenderStep();

        step.WaitForElement(".kh-wizard-ffmpeg__next");
        Assert.Contains("9.0", step.Find("[data-tool='FFmpeg']").TextContent);
        _ffmpeg.DidNotReceive().InstallAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void InstallFails_OffersRetryAndAWayPast()
    {
        _ffmpeg.CheckAsync(Arg.Any<CancellationToken>()).Returns(AppSettingsPageServices.Missing());
        _ffmpeg.InstallAsync(Arg.Any<CancellationToken>()).Returns(Failed(), Installed());

        var step = RenderStep();

        step.WaitForElement(".kh-wizard-ffmpeg__download").Click();

        step.WaitForElement(".kh-wizard-ffmpeg__retry");
        Assert.Equal("Could not reach the publisher.", step.Find(".kh-ffmpeg-status__error").TextContent);
        Assert.Contains("will not play until FFmpeg is installed", step.Markup);

        step.Find(".kh-wizard-ffmpeg__retry").Click();

        step.WaitForElement(".kh-wizard-ffmpeg__next");
        _ffmpeg.Received(2).InstallAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void InstallFails_ContinueWithout_MovesOn()
    {
        _ffmpeg.CheckAsync(Arg.Any<CancellationToken>()).Returns(AppSettingsPageServices.Missing());
        _ffmpeg.InstallAsync(Arg.Any<CancellationToken>()).Returns(Failed());

        var step = RenderStep();

        step.WaitForElement(".kh-wizard-ffmpeg__download").Click();
        step.WaitForElement(".kh-wizard-ffmpeg__skip").Click();

        Assert.Equal(1, _completed);
    }

    [Fact]
    public void NoBuildForThisComputer_DownloadsNothing_AndLetsTheHostPast()
    {
        _ffmpeg.CanInstall.Returns(false);
        _ffmpeg.CheckAsync(Arg.Any<CancellationToken>()).Returns(AppSettingsPageServices.Missing());

        var step = RenderStep();

        step.WaitForElement(".kh-wizard-ffmpeg__skip");
        Assert.Empty(step.FindAll(".kh-wizard-ffmpeg__download"));
        Assert.Empty(step.FindAll(".kh-wizard-ffmpeg__retry"));
        _ffmpeg.DidNotReceive().InstallAsync(Arg.Any<CancellationToken>());
    }

    private IRenderedComponent<WizardStepFFmpeg> RenderStep()
        => Render<WizardStepFFmpeg>(parameters => parameters.Add(p => p.OnComplete, () => _completed++));

    private static FFmpegStatus Installed() => AppSettingsPageServices.Found() with
    {
        Install = new FFmpegInstallProgress { State = FFmpegInstallState.Succeeded },
    };

    private static FFmpegStatus Failed() => AppSettingsPageServices.Missing() with
    {
        Install = new FFmpegInstallProgress { State = FFmpegInstallState.Failed, Error = "Could not reach the publisher." },
    };
}
