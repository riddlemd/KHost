using KHost.UserInterface.Services;

namespace KHost.UnitTests.UserInterface.Services;

public class AppInfoServiceTests
{
    private readonly AppInfoService _service = new();

    [Fact]
    public void Version_MatchesTheAssemblyVersion()
    {
        var expected = typeof(AppInfoService).Assembly.GetName().Version?.ToString();

        Assert.Equal(expected, _service.Version);
    }

    [Fact]
    public void LicenseText_StartsWithTheFirstLineOfLICENSE()
    {
        Assert.StartsWith("KHost", _service.LicenseText);
    }

    [Fact]
    public void LicenseName_IsThePolyFormShieldHeading()
    {
        Assert.Equal("PolyForm Shield License 1.0.0", _service.LicenseName);
    }

    [Fact]
    public void Copyright_NamesTheAuthor()
    {
        Assert.Contains("Michael Riddle", _service.Copyright);
        Assert.StartsWith("Copyright", _service.Copyright);
    }

    [Fact]
    public void ThirdPartyNotices_StartsWithTheFirstLineOfTheNoticesFile()
    {
        Assert.StartsWith("# Third-Party Notices", _service.ThirdPartyNotices);
    }

    [Fact]
    public void ThirdPartyNotices_ListsAKnownComponent()
    {
        Assert.Contains("FFMpegCore", _service.ThirdPartyNotices);
    }

    [Fact]
    public void ReferencedLicenses_EachHaveNonEmptyText()
    {
        Assert.Equal(3, _service.ReferencedLicenses.Count);
        Assert.All(_service.ReferencedLicenses, license => Assert.False(string.IsNullOrWhiteSpace(license.Text)));
    }

    [Fact]
    public void ReferencedLicenses_TextsMatchTheirNamedLicense()
    {
        Assert.Contains(_service.ReferencedLicenses, l => l.Name.Contains("Apache") && l.Text.Contains("Apache License"));
        Assert.Contains(_service.ReferencedLicenses, l => l.Name.Contains("LGPL") && l.Text.Contains("GNU LESSER GENERAL PUBLIC LICENSE"));
        Assert.Contains(_service.ReferencedLicenses, l => l.Name.Contains("Open Font") && l.Text.Contains("Reserved Font Name"));
    }

    [Fact]
    public void RepositoryUrl_MatchesTheCsprojsRepositoryUrl()
    {
        // Same URL `git remote get-url origin` resolves to (https://github.com/riddlemd/KHost.git),
        // minus the .git suffix — verified against the csproj's <RepositoryUrl>, not hardcoded here.
        Assert.Equal("https://github.com/riddlemd/KHost", _service.RepositoryUrl);
    }

    [Fact]
    public void IssuesUrl_IsTheRepositoryUrlPlusIssues()
    {
        Assert.Equal(_service.RepositoryUrl + "/issues", _service.IssuesUrl);
    }

    [Fact]
    public void WikiUrl_IsTheRepositoryUrlPlusWiki()
    {
        Assert.Equal(_service.RepositoryUrl + "/wiki", _service.WikiUrl);
    }

    [Fact]
    public void ContributingUrl_PointsAtCONTRIBUTINGOnTheDefaultBranch()
    {
        Assert.Equal(_service.RepositoryUrl + "/blob/master/CONTRIBUTING.md", _service.ContributingUrl);
    }
}
