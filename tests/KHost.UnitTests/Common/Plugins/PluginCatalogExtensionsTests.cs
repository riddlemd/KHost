using KHost.Abstractions.Models.Plugins;
using KHost.Common.Plugins;

namespace KHost.UnitTests.Common.Plugins;

public class PluginCatalogExtensionsTests
{
    [Fact]
    public void LatestCompatible_SeveralReleases_ReturnsHighestVersion()
    {
        var entry = EntryWith(Release("1.0.0"), Release("1.10.0"), Release("1.9.0"));

        Assert.Equal("1.10.0", entry.LatestCompatibleRelease()?.Version);
    }

    [Fact]
    public void LatestCompatible_NewerReleaseBuiltForANewerApi_ReturnsTheOneInRange()
    {
        var entry = EntryWith(Release("1.0.0"), Release("2.0.0", apiVersion: PluginApi.CurrentVersion + 1));

        Assert.Equal("1.0.0", entry.LatestCompatibleRelease()?.Version);
    }

    [Fact]
    public void LatestCompatible_NewerReleaseBuiltForAnOlderApi_ReturnsTheOneInRange()
    {
        var entry = EntryWith(Release("1.0.0"), Release("2.0.0", apiVersion: PluginApi.MinimumVersion - 1));

        Assert.Equal("1.0.0", entry.LatestCompatibleRelease()?.Version);
    }

    [Theory]
    [InlineData(PluginApi.CurrentVersion + 1)]
    [InlineData(PluginApi.MinimumVersion - 1)]
    public void LatestCompatible_EveryReleaseOutOfRange_ReturnsNull(int apiVersion)
        => Assert.Null(EntryWith(Release("1.0.0", apiVersion: apiVersion)).LatestCompatibleRelease());

    [Fact]
    public void LatestCompatible_SeveralApisInRange_ReturnsTheHighestPluginVersion()
    {
        // Plugin version decides, not plugin API: a newer release may be built against an older API.
        var entry = EntryWith(
            Release("3.0.0", apiVersion: 3),
            Release("4.0.0", apiVersion: 4),
            Release("5.0.0", apiVersion: 2),
            Release("6.0.0", apiVersion: 5));

        Assert.Equal("5.0.0", entry.LatestCompatibleRelease(new PluginApiRange(2, 4))?.Version);
    }

    [Fact]
    public void LatestCompatible_ReleasesAtBothEndsOfTheRange_AreBothCandidates()
    {
        var range = new PluginApiRange(2, 4);

        Assert.Equal("2.0.0", EntryWith(Release("1.0.0", apiVersion: 4), Release("2.0.0", apiVersion: 2)).LatestCompatibleRelease(range)?.Version);
        Assert.Equal("2.0.0", EntryWith(Release("1.0.0", apiVersion: 2), Release("2.0.0", apiVersion: 4)).LatestCompatibleRelease(range)?.Version);
    }

    [Fact]
    public void DescribeApiRefusal_NewestApiAboveTheRange_SaysKHostNeedsTheUpdate()
    {
        var entry = EntryWith(Release("1.0.0", apiVersion: PluginApi.MinimumVersion - 1), Release("2.0.0", apiVersion: PluginApi.CurrentVersion + 1));

        Assert.StartsWith("Needs a newer KHost", entry.DescribeApiRefusal());
    }

    [Fact]
    public void DescribeApiRefusal_EveryReleaseBelowTheRange_SaysThePluginNeedsTheUpdate()
        => Assert.EndsWith("it needs an update.", EntryWith(Release("1.0.0", apiVersion: PluginApi.MinimumVersion - 1)).DescribeApiRefusal());

    [Fact]
    public void DescribeApiRefusal_AReleaseInRange_IsNull()
        => Assert.Null(EntryWith(Release("1.0.0", apiVersion: PluginApi.CurrentVersion + 1), Release("0.9.0")).DescribeApiRefusal());

    [Fact]
    public void DescribeApiRefusal_NoReleases_IsNull()
        => Assert.Null(EntryWith().DescribeApiRefusal());

    [Fact]
    public void LatestCompatible_NewestReleaseHasNoChecksum_SkipsIt()
    {
        var entry = EntryWith(Release("1.0.0"), Release("2.0.0", sha256: ""));

        Assert.Equal("1.0.0", entry.LatestCompatibleRelease()?.Version);
    }

    [Fact]
    public void LatestCompatible_NewestReleaseIsNotHttps_SkipsIt()
    {
        var entry = EntryWith(Release("1.0.0"), Release("2.0.0", url: "http://example.test/plugin.zip"));

        Assert.Equal("1.0.0", entry.LatestCompatibleRelease()?.Version);
    }

    [Fact]
    public void HasReleaseForThisHost_ReleaseWithoutAChecksum_IsStillForThisHost()
    {
        // The distinction the browse list needs: nothing is installable, but telling the host it
        // is "not compatible" would send them hunting for a KHost upgrade that changes nothing.
        var entry = EntryWith(Release("1.0.0", sha256: ""));

        Assert.True(entry.HasReleaseForThisHost());
        Assert.Null(entry.LatestCompatibleRelease());
    }

    [Theory]
    [InlineData(PluginApi.CurrentVersion + 1)]
    [InlineData(PluginApi.MinimumVersion - 1)]
    public void HasReleaseForThisHost_EveryReleaseOutOfRange_IsFalse(int apiVersion)
        => Assert.False(EntryWith(Release("1.0.0", apiVersion: apiVersion)).HasReleaseForThisHost());

    [Theory]
    [InlineData(PluginApi.CurrentVersion + 1)]
    [InlineData(PluginApi.MinimumVersion - 1)]
    public void HasReleaseForThisPlatform_NeutralBuildOutOfRange_IsFalse(int apiVersion)
        => Assert.False(EntryWith(Release("1.0.0", apiVersion: apiVersion)).HasReleaseForThisPlatform());

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

        Assert.Equal("1.0.0", entry.LatestCompatibleRelease()?.Version);
    }

    [Fact]
    public void LatestCompatible_OnlyBuildsForOtherPlatforms_ReturnsNull()
    {
        var other = PluginRid.Current == "win" ? "linux" : "win";

        Assert.Null(EntryWith(Release("1.0.0", rid: other)).LatestCompatibleRelease());
    }

    [Fact]
    public void LatestCompatible_SameVersionNeutralAndPlatform_PrefersThePlatformBuild()
    {
        // At one version the platform build is the more capable package: it exists precisely
        // because some OS API needed it.
        var entry = EntryWith(Release("1.0.0"), Release("1.0.0", rid: PluginRid.Current, url: "https://example.test/p2.zip"));

        Assert.Equal(PluginRid.Current, entry.LatestCompatibleRelease()?.Rid);
    }

    [Fact]
    public void LatestCompatible_NewerNeutralThanPlatformBuild_TakesTheNewer()
    {
        var entry = EntryWith(Release("1.0.0", rid: PluginRid.Current), Release("2.0.0"));

        Assert.Equal("2.0.0", entry.LatestCompatibleRelease()?.Version);
        Assert.Null(entry.LatestCompatibleRelease()?.Rid);
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
        => Assert.Null(EntryWith().LatestCompatibleRelease());

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
