using KHost.UserInterface.Components;

namespace KHost.UnitTests.UserInterface.Components;

public class SettingsButtonTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("/settings/tips-manager", "/settings/tips-manager")]
    // A route from the menu and a path from the browser need not agree about a trailing slash.
    [InlineData("/settings/tips-manager/", "/settings/tips-manager")]
    [InlineData("/Settings/Tips-Manager", "/settings/tips-manager")]
    public void IsSameRoute_MatchesTheRouteYouAreOn(string path, string route)
    {
        Assert.True(SettingsButton.IsSameRoute(path, route));
    }

    [Theory]
    [InlineData("/settings/tips-manager", "/settings/users-manager")]
    [InlineData("/settings/tips-manager", "/")]
    [InlineData("/", "/settings/tips-manager")]
    // Otherwise every settings page would mark the one whose route it starts with.
    [InlineData("/settings/tips-manager-archive", "/settings/tips-manager")]
    public void IsSameRoute_RejectsAnyOther(string path, string route)
    {
        Assert.False(SettingsButton.IsSameRoute(path, route));
    }
}
