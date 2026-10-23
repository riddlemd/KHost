using KHost.UserInterface.Services;

namespace KHost.UnitTests.UserInterface.Services;

public sealed class AppUpdateServiceTests
{
    [Theory]
    [InlineData("https://example.com/releases", true, true)]
    [InlineData("https://example.com/releases", false, false)]
    [InlineData(null, true, false)]
    [InlineData("", true, false)]
    [InlineData("   ", true, false)]
    public void Given_AFeedAndAnInstall_When_DecidingToCheck_Then_OnlyAVelopackInstallWithAFeedChecks(string? feedUrl, bool isInstalled, bool expected)
    {
        // Given: the configured feed and whether Velopack installed this copy (the parameters)

        // When
        var checks = AppUpdateService.ShouldCheck(feedUrl, isInstalled);

        // Then
        Assert.Equal(expected, checks);
    }
}
