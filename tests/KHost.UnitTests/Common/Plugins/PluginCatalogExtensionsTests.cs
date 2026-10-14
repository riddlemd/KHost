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

    /// <summary>Requirements this host refuses: the next minor and major, an older major, and text that is
    /// not a version.</summary>
    public static TheoryData<string> ContractsThisHostDoesNotHave()
    {
        var host = ContractsVersion.ThisHost;

        return new TheoryData<string>($"{host.Major}.{host.Minor + 1}.0", $"{host.Major + 1}.0.0", "0.60.0", "not-a-version");
    }

    [Fact]
    public void LatestCompatible_NewerReleaseNeedsANewerMinor_ReturnsTheOneThisHostRuns()
    {
        var host = ContractsVersion.ThisHost;
        var entry = EntryWith(Release("1.0.0"), Release("2.0.0", contracts: $"{host.Major}.{host.Minor + 1}.0"));

        Assert.Equal("1.0.0", entry.LatestCompatibleRelease()?.Version);
    }

    [Fact]
    public void LatestCompatible_NewerReleaseNeedsAnOlderMajor_ReturnsTheOneThisHostRuns()
    {
        var entry = EntryWith(Release("1.0.0"), Release("2.0.0", contracts: "0.60.0"));

        Assert.Equal("1.0.0", entry.LatestCompatibleRelease()?.Version);
    }

    [Theory]
    [MemberData(nameof(ContractsThisHostDoesNotHave))]
    public void LatestCompatible_EveryReleaseNeedsOtherContracts_ReturnsNull(string contracts)
        => Assert.Null(EntryWith(Release("1.0.0", contracts: contracts)).LatestCompatibleRelease());

    [Fact]
    public void LatestCompatible_SeveralRequirementsThisHostHas_ReturnsTheHighestPluginVersion()
    {
        // Plugin version decides, not contracts: a newer release may need older contracts.
        var entry = EntryWith(
            Release("3.0.0", contracts: "2.3.0"),
            Release("4.0.0", contracts: "2.4.0"),
            Release("5.0.0", contracts: "2.0.0"),
            Release("6.0.0", contracts: "2.4.1"),
            Release("7.0.0", contracts: "3.0.0"));

        Assert.Equal("5.0.0", entry.LatestCompatibleRelease(new ContractsVersion(2, 4, 0))?.Version);
    }

    [Fact]
    public void LatestCompatible_ReleaseNeedingExactlyTheHostsVersion_IsACandidate()
        => Assert.Equal("1.0.0", EntryWith(Release("1.0.0", contracts: "2.4.0")).LatestCompatibleRelease(new ContractsVersion(2, 4, 0))?.Version);

    [Fact]
    public void LatestCompatible_LegacyApiVersion8Only_IsACandidateOnA1xHost()
        => Assert.Equal("1.0.0", EntryWith(Release("1.0.0", contracts: null, apiVersion: 8)).LatestCompatibleRelease(new ContractsVersion(1, 4, 0))?.Version);

    [Theory]
    [InlineData(7)]
    [InlineData(9)]
    public void LatestCompatible_LegacyApiVersionOtherThan8_IsNotACandidate(int apiVersion)
        => Assert.Null(EntryWith(Release("1.0.0", contracts: null, apiVersion: apiVersion)).LatestCompatibleRelease(new ContractsVersion(1, 4, 0)));

    [Fact]
    public void LatestCompatible_LegacyApiVersion8OnAnotherMajor_IsNotACandidate()
        => Assert.Null(EntryWith(Release("1.0.0", contracts: null, apiVersion: 8)).LatestCompatibleRelease(new ContractsVersion(2, 0, 0)));

    [Fact]
    public void LatestCompatible_ContractsVersionPresent_WinsOverLegacyApiVersion()
        => Assert.Null(EntryWith(Release("1.0.0", contracts: "2.0.0", apiVersion: 8)).LatestCompatibleRelease(new ContractsVersion(1, 4, 0)));

    [Fact]
    public void RequiredContracts_ContractsVersionPresent_IsThatVersion()
        => Assert.Equal(new ContractsVersion(1, 2, 3), Release("1.0.0", contracts: "1.2.3", apiVersion: 8).RequiredContracts());

    [Fact]
    public void RequiredContracts_ContractsVersionMalformed_IsNullEvenWithLegacyApiVersion8()
        => Assert.Null(Release("1.0.0", contracts: "1.2", apiVersion: 8).RequiredContracts());

    [Fact]
    public void RequiredContracts_OnlyLegacyApiVersion8_IsTheLegacyContracts()
        => Assert.Equal(new ContractsVersion(0, 61, 0), Release("1.0.0", contracts: null, apiVersion: 8).RequiredContracts());

    [Fact]
    public void RequiredContracts_OnlyLegacyApiVersion9_IsNull()
        => Assert.Null(Release("1.0.0", contracts: null, apiVersion: 9).RequiredContracts());

    [Fact]
    public void DescribeApiRefusal_NewestRequirementIsNewer_SaysKHostNeedsTheUpdate()
    {
        var entry = EntryWith(Release("1.0.0", contracts: "0.60.0"), Release("2.0.0", contracts: $"{ContractsVersion.ThisHost.Major + 1}.0.0"));

        Assert.StartsWith("Needs a newer KHost", entry.DescribeApiRefusal());
    }

    [Fact]
    public void DescribeApiRefusal_EveryReleaseNeedsAnOlderMajor_SaysThePluginNeedsTheUpdate()
        => Assert.EndsWith("it needs an update.", EntryWith(Release("1.0.0", contracts: "0.60.0")).DescribeApiRefusal());

    [Fact]
    public void DescribeApiRefusal_OnlyAnOlderLegacyApiVersion_SaysThePluginNeedsTheUpdate()
        => Assert.EndsWith("it needs an update.", EntryWith(Release("1.0.0", contracts: null, apiVersion: 7)).DescribeApiRefusal());

    [Fact]
    public void DescribeApiRefusal_AReleaseThisHostRuns_IsNull()
        => Assert.Null(EntryWith(Release("1.0.0", contracts: $"{ContractsVersion.ThisHost.Major + 1}.0.0"), Release("0.9.0")).DescribeApiRefusal());

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
    [MemberData(nameof(ContractsThisHostDoesNotHave))]
    public void HasReleaseForThisHost_EveryReleaseNeedsOtherContracts_IsFalse(string contracts)
        => Assert.False(EntryWith(Release("1.0.0", contracts: contracts)).HasReleaseForThisHost());

    [Theory]
    [MemberData(nameof(ContractsThisHostDoesNotHave))]
    public void HasReleaseForThisPlatform_NeutralBuildNeedsOtherContracts_IsFalse(string contracts)
        => Assert.False(EntryWith(Release("1.0.0", contracts: contracts)).HasReleaseForThisPlatform());

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

    // A const stand-in, since an optional parameter cannot default to a value read at run time.
    private const string ThisHostsContracts = "this-host";

    private static PluginCatalogRelease Release(
        string version,
        string? contracts = ThisHostsContracts,
        int apiVersion = 9,
        string sha256 = "abc123",
        string url = "https://example.test/plugin.zip",
        string? rid = null) => new()
    {
        Version = version,
        ContractsVersion = contracts == ThisHostsContracts ? ContractsVersion.ThisHost.ToString() : contracts,
        ApiVersion = apiVersion,
        Url = url,
        Sha256 = sha256,
        Rid = rid,
    };
}
