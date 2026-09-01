using KHost.Abstractions.Models.Plugins;
using KHost.Common.Plugins;

namespace KHost.UnitTests.Common.Plugins;

public class PluginRidTests
{
    [Fact]
    public void Matches_NoRid_RunsAnywhere()
    {
        Assert.True(PluginRid.Matches(null));
        Assert.True(PluginRid.Matches(""));
        Assert.True(PluginRid.Matches("   "));
    }

    [Fact]
    public void Matches_ThisPlatform_IsTrue()
        => Assert.True(PluginRid.Matches(PluginRid.Current));

    [Fact]
    public void Matches_ThisPlatformAndArchitecture_IsTrue()
        => Assert.True(PluginRid.Matches($"{PluginRid.Current}-{PluginRid.CurrentArchitecture}"));

    [Fact]
    public void Matches_ThisPlatformWithAnotherArchitecture_IsFalse()
    {
        var other = PluginRid.CurrentArchitecture == "arm64" ? "x64" : "arm64";

        Assert.False(PluginRid.Matches($"{PluginRid.Current}-{other}"));
    }

    [Fact]
    public void Matches_AnotherPlatform_IsFalse()
    {
        var other = PluginRid.Current == "win" ? "linux" : "win";

        Assert.False(PluginRid.Matches(other));
    }

    [Theory]
    [InlineData("win10-x64")]
    [InlineData("linux-musl-arm64")]
    [InlineData("nonsense")]
    public void Matches_ARidThisHostDoesNotModel_IsFalse(string rid)
        => Assert.False(PluginRid.Matches(rid));

    [Fact]
    public void Current_IsOneOfThePlatformsTheCatalogAllows()
        => Assert.True(PluginRid.IsKnown(PluginRid.Current));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("win")]
    [InlineData("osx")]
    [InlineData("linux")]
    [InlineData("win-x64")]
    [InlineData("osx-arm64")]
    public void IsKnown_ASpellingTheCatalogAllows_IsTrue(string? rid)
        => Assert.True(PluginRid.IsKnown(rid));

    [Theory]
    [InlineData("windows")]
    [InlineData("macos")]
    [InlineData("win10-x64")]
    [InlineData("linux-musl-arm64")]
    [InlineData("win-sparc")]
    [InlineData("win-x64-extra")]
    public void IsKnown_ASpellingTheCatalogRefuses_IsFalse(string rid)
        => Assert.False(PluginRid.IsKnown(rid));
}
