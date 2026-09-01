using KHost.Abstractions.Models.Plugins;
using KHost.Common.Plugins;
using KHost.Plugins.Sdk;

namespace KHost.UnitTests.Common.Plugins;

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
    public void Serializing_ARelease_DoesNotWriteTheDerivedInstallableFlag()
    {
        // The sync tool writes the catalog back out, so anything derived that serialises ends up
        // published as though a publisher had set it.
        var json = System.Text.Json.JsonSerializer.Serialize(Release("1.0.0"), System.Text.Json.JsonSerializerOptions.Web);

        Assert.DoesNotContain("installable", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sha256", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LatestCompatible_ABuildForAnotherPlatform_IsSkipped()
    {
        var other = PluginRid.Current == "win" ? "linux" : "win";
        var entry = EntryWith(Release("1.0.0"), Release("2.0.0", rid: other));

        Assert.Equal("1.0.0", entry.LatestCompatible()?.Version);
    }

    [Fact]
    public void LatestCompatible_OnlyBuildsForOtherPlatforms_ReturnsNull()
    {
        var other = PluginRid.Current == "win" ? "linux" : "win";

        Assert.Null(EntryWith(Release("1.0.0", rid: other)).LatestCompatible());
    }

    [Fact]
    public void LatestCompatible_SameVersionNeutralAndPlatform_PrefersThePlatformBuild()
    {
        // At one version the platform build is the more capable package — it exists precisely
        // because some OS API needed it.
        var entry = EntryWith(Release("1.0.0"), Release("1.0.0", rid: PluginRid.Current, url: "https://example.test/p2.zip"));

        Assert.Equal(PluginRid.Current, entry.LatestCompatible()?.Rid);
    }

    [Fact]
    public void LatestCompatible_NewerNeutralThanPlatformBuild_TakesTheNewer()
    {
        var entry = EntryWith(Release("1.0.0", rid: PluginRid.Current), Release("2.0.0"));

        Assert.Equal("2.0.0", entry.LatestCompatible()?.Version);
        Assert.Null(entry.LatestCompatible()?.Rid);
    }

    [Fact]
    public void HasReleaseForThisPlatform_OnlyOtherPlatforms_IsFalse()
    {
        var other = PluginRid.Current == "win" ? "linux" : "win";
        var entry = EntryWith(Release("1.0.0", rid: other));

        Assert.True(entry.HasReleaseForThisHost());
        Assert.False(entry.HasReleaseForThisPlatform());
    }

    [Fact]
    public void HasReleaseForThisPlatform_ANeutralBuild_IsTrue()
        => Assert.True(EntryWith(Release("1.0.0")).HasReleaseForThisPlatform());

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
        string url = "https://example.test/plugin.zip",
        string? rid = null) => new()
    {
        Version = version,
        ApiVersion = apiVersion ?? PluginApi.CurrentVersion,
        Url = url,
        Sha256 = sha256,
        Rid = rid,
    };
}
