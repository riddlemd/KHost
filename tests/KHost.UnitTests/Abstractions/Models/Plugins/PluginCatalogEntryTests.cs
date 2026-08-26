using KHost.Abstractions.Models.Plugins;
using KHost.Plugins.Sdk;

namespace KHost.UnitTests.Abstractions.Models.Plugins;

public class PluginCatalogEntryTests
{
    [Fact]
    public void LatestCompatible_SeveralReleases_ReturnsHighestVersion()
    {
        var entry = EntryWith(Release("1.0.0"), Release("1.10.0"), Release("1.9.0"));

        Assert.Equal("1.10.0", entry.LatestCompatible()?.Version);
    }

    [Fact]
    public void LatestCompatible_NewerReleaseTargetsAnotherApi_ReturnsCompatibleOne()
    {
        var entry = EntryWith(Release("1.0.0"), Release("2.0.0", apiVersion: PluginApi.CurrentVersion + 1));

        Assert.Equal("1.0.0", entry.LatestCompatible()?.Version);
    }

    [Fact]
    public void LatestCompatible_EveryReleaseTargetsAnotherApi_ReturnsNull()
    {
        var entry = EntryWith(Release("1.0.0", apiVersion: PluginApi.CurrentVersion + 1));

        Assert.Null(entry.LatestCompatible());
    }

    [Fact]
    public void LatestCompatible_NewestReleaseHasNoChecksum_SkipsIt()
    {
        var entry = EntryWith(Release("1.0.0"), Release("2.0.0", sha256: ""));

        Assert.Equal("1.0.0", entry.LatestCompatible()?.Version);
    }

    [Fact]
    public void LatestCompatible_NewestReleaseIsNotHttps_SkipsIt()
    {
        var entry = EntryWith(Release("1.0.0"), Release("2.0.0", url: "http://example.test/plugin.zip"));

        Assert.Equal("1.0.0", entry.LatestCompatible()?.Version);
    }

    [Fact]
    public void HasReleaseForThisHost_ReleaseWithoutAChecksum_IsStillForThisHost()
    {
        // The distinction the browse list needs: nothing is installable, but telling the host it
        // is "not compatible" would send them hunting for a KHost upgrade that changes nothing.
        var entry = EntryWith(Release("1.0.0", sha256: ""));

        Assert.True(entry.HasReleaseForThisHost());
        Assert.Null(entry.LatestCompatible());
    }

    [Fact]
    public void HasReleaseForThisHost_EveryReleaseTargetsAnotherApi_IsFalse()
        => Assert.False(EntryWith(Release("1.0.0", apiVersion: PluginApi.CurrentVersion + 1)).HasReleaseForThisHost());

    [Fact]
    public void HasReleaseForThisHost_NoReleases_IsFalse()
        => Assert.False(EntryWith().HasReleaseForThisHost());

    [Fact]
    public void LatestCompatible_NoReleases_ReturnsNull()
        => Assert.Null(EntryWith().LatestCompatible());

    private static PluginCatalogEntry EntryWith(params PluginCatalogRelease[] releases) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Plugin",
        Releases = [.. releases],
    };

    private static PluginCatalogRelease Release(
        string version,
        int? apiVersion = null,
        string sha256 = "abc123",
        string url = "https://example.test/plugin.zip") => new()
    {
        Version = version,
        ApiVersion = apiVersion ?? PluginApi.CurrentVersion,
        Url = url,
        Sha256 = sha256,
    };
}
