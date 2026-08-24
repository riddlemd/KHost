using Bunit;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

public class AboutPageTests : BunitContext
{
    private const string ToggleSelector = ".kh-about__toggle";
    private const string LinkSelector = ".kh-about__link";

    private readonly IAppInfoService _appInfo = Substitute.For<IAppInfoService>();
    private readonly IExternalLinkService _externalLinks = Substitute.For<IExternalLinkService>();

    public AboutPageTests()
    {
        _appInfo.Version.Returns("9.9.9.9");
        _appInfo.Copyright.Returns("Copyright (c) 2026 Michael Riddle");
        _appInfo.LicenseName.Returns("PolyForm Shield License 1.0.0");
        _appInfo.LicenseText.Returns("PolyForm Shield License full text goes here.");
        _appInfo.ThirdPartyNotices.Returns("FFMpegCore | host, screen | MIT | Malte Rosenbjerg and contributors");
        _appInfo.ReferencedLicenses.Returns(
        [
            new ReferencedLicense("Apache License 2.0", "Apache full text."),
        ]);
        _appInfo.RepositoryUrl.Returns("https://github.com/riddlemd/KHost");
        _appInfo.IssuesUrl.Returns("https://github.com/riddlemd/KHost/issues");
        _appInfo.WikiUrl.Returns("https://github.com/riddlemd/KHost/wiki");
        _appInfo.ContributingUrl.Returns("https://github.com/riddlemd/KHost/blob/master/CONTRIBUTING.md");

        Services.AddSingleton(_appInfo);
        Services.AddSingleton(_externalLinks);
    }

    [Fact]
    public void Renders_TheVersionFromTheService()
    {
        var cut = Render<AboutPage>();

        Assert.Contains("9.9.9.9", cut.Markup);
    }

    [Fact]
    public void LicenseSection_IsCollapsedByDefault()
    {
        var cut = Render<AboutPage>();

        Assert.DoesNotContain("PolyForm Shield License 1.0.0", cut.Markup);
    }

    [Fact]
    public async Task LicenseToggle_Clicked_RevealsTheLicenseName()
    {
        var cut = Render<AboutPage>();

        await cut.InvokeAsync(() => cut.FindAll(ToggleSelector)[0].Click());

        Assert.Contains("PolyForm Shield License 1.0.0", cut.Markup);
    }

    [Fact]
    public void NoticesSection_IsCollapsedByDefault()
    {
        var cut = Render<AboutPage>();

        Assert.DoesNotContain("FFMpegCore", cut.Markup);
    }

    [Fact]
    public async Task NoticesToggle_Clicked_RevealsAKnownThirdPartyComponent()
    {
        var cut = Render<AboutPage>();

        await cut.InvokeAsync(() => cut.FindAll(ToggleSelector)[1].Click());

        Assert.Contains("FFMpegCore", cut.Markup);
    }

    [Fact]
    public async Task RepositoryLink_Clicked_OpensItThroughTheExternalLinkService()
    {
        var cut = Render<AboutPage>();

        await cut.InvokeAsync(() => cut.FindAll(LinkSelector)
            .Single(link => link.TextContent.Contains("GitHub Repository")).Click());

        _externalLinks.Received(1).Open("https://github.com/riddlemd/KHost");
    }

    [Fact]
    public async Task IssueLink_Clicked_OpensTheIssuesUrl()
    {
        var cut = Render<AboutPage>();

        await cut.InvokeAsync(() => cut.FindAll(LinkSelector)
            .Single(link => link.TextContent.Contains("Report an Issue")).Click());

        _externalLinks.Received(1).Open("https://github.com/riddlemd/KHost/issues");
    }
}
