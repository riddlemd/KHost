using System.Xml.Linq;
using KHost.Common.Plugins;

namespace KHost.UnitTests.Common.Plugins;

public class ContractsVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("0.61.0", 0, 61, 0, null)]
    [InlineData("10.20.30", 10, 20, 30, null)]
    [InlineData("1.0.0-alpha.1", 1, 0, 0, "alpha.1")]
    [InlineData("1.0.0-x-y.0", 1, 0, 0, "x-y.0")]
    [InlineData("1.0.0+build.5", 1, 0, 0, null)]
    [InlineData("1.0.0-rc.1+build.007", 1, 0, 0, "rc.1")]
    [InlineData(" 2.0.0 ", 2, 0, 0, null)]
    public void Parse_ASemanticVersion_ReadsEveryPart(string text, int major, int minor, int patch, string? prerelease)
    {
        var version = ContractsVersion.Parse(text);

        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(prerelease, version.Prerelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.03")]
    [InlineData("-1.2.3")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3-a..b")]
    [InlineData("1.2.3-a_b")]
    [InlineData("1.2.3+")]
    [InlineData("99999999999.0.0")]
    public void TryParse_NotASemanticVersion_IsFalse(string? text)
        => Assert.False(ContractsVersion.TryParse(text, out _));

    [Fact]
    public void Parse_NotASemanticVersion_Throws()
        => Assert.Throws<FormatException>(() => ContractsVersion.Parse("1.2"));

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.0.0-alpha.1")]
    public void ToString_RoundTripsWhatWasParsed(string text)
        => Assert.Equal(text, ContractsVersion.Parse(text).ToString());

    [Fact]
    public void ToString_DropsTheBuildLabel()
        => Assert.Equal("1.0.0", ContractsVersion.Parse("1.0.0+abc").ToString());

    /// <summary>The ordering in the semantic versioning spec's own example, plus each number column.</summary>
    [Theory]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
    [InlineData("1.0.0-beta", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-beta.11", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.0.9", "1.1.0")]
    [InlineData("1.9.9", "2.0.0")]
    [InlineData("0.61.0", "1.0.0")]
    [InlineData("1.2.0", "1.10.0")]
    public void CompareTo_OrdersAsSemanticVersioningDoes(string lower, string higher)
    {
        var low = ContractsVersion.Parse(lower);
        var high = ContractsVersion.Parse(higher);

        Assert.True(low < high);
        Assert.True(high > low);
        Assert.True(low <= high);
        Assert.False(low >= high);
        Assert.True(low.CompareTo(high) < 0);
        Assert.True(high.CompareTo(low) > 0);
    }

    [Fact]
    public void CompareTo_TheBuildLabel_IsIgnored()
    {
        var a = ContractsVersion.Parse("1.2.3+one");
        var b = ContractsVersion.Parse("1.2.3+two");

        Assert.Equal(0, a.CompareTo(b));
        Assert.Equal(a, b);
        Assert.True(a <= b && a >= b);
    }

    [Theory]
    [InlineData("1.4.2")]
    [InlineData("1.4.0")]
    [InlineData("1.0.0")]
    [InlineData("1.3.9")]
    public void Covers_SameMajorNoNewer_IsTrue(string required)
        => Assert.True(ContractsVersion.Parse("1.4.2").Covers(ContractsVersion.Parse(required)));

    [Theory]
    [InlineData("1.4.3")]
    [InlineData("1.5.0")]
    [InlineData("2.0.0")]
    [InlineData("0.9.0")]
    [InlineData("0.60.0")]
    [InlineData("0.62.0")]
    public void Covers_AnotherMajorOrNewer_IsFalse(string required)
        => Assert.False(ContractsVersion.Parse("1.4.2").Covers(ContractsVersion.Parse(required)));

    [Fact]
    public void Covers_OnAnOlderMajorHost_RefusesTheNextMajorsFirstRelease()
        => Assert.False(ContractsVersion.Parse("2.9.9").Covers(ContractsVersion.Parse("3.0.0")));

    [Fact]
    public void Covers_ANewerHostMajor_RefusesAnOlderMajorsRequirement()
        => Assert.False(ContractsVersion.Parse("3.0.0").Covers(ContractsVersion.Parse("2.9.9")));

    [Theory]
    [InlineData("0.61.0")]
    [InlineData("0.61.7")]
    public void Covers_Legacy061_RunsOnEvery1xHost(string required)
    {
        Assert.True(ContractsVersion.Parse("1.0.0").Covers(ContractsVersion.Parse(required)));
        Assert.True(ContractsVersion.Parse("1.9.0").Covers(ContractsVersion.Parse(required)));
    }

    [Fact]
    public void Covers_Legacy061_DoesNotRunOnA2xHost()
        => Assert.False(ContractsVersion.Parse("2.0.0").Covers(ContractsVersion.Parse("0.61.0")));

    [Fact]
    public void Covers_APrereleaseOfTheHostsVersion_IsTrue()
        => Assert.True(ContractsVersion.Parse("1.1.0").Covers(ContractsVersion.Parse("1.1.0-beta")));

    [Fact]
    public void Covers_TheReleaseOnAPrereleaseHost_IsFalse()
        => Assert.False(ContractsVersion.Parse("1.1.0-beta").Covers(ContractsVersion.Parse("1.1.0")));

    [Fact]
    public void DescribeRefusal_Covered_IsNull()
        => Assert.Null(ContractsVersion.Parse("1.4.2").DescribeRefusal(ContractsVersion.Parse("1.4.2")));

    [Theory]
    [InlineData("1.4.3")]
    [InlineData("2.0.0")]
    public void DescribeRefusal_NeedsNewer_SaysKHostNeedsTheUpdate(string required)
        => Assert.Equal(
            $"Needs a newer KHost (needs contracts {required}; this KHost has 1.4.2).",
            ContractsVersion.Parse("1.4.2").DescribeRefusal(ContractsVersion.Parse(required)));

    [Fact]
    public void DescribeRefusal_AnOlderMajor_SaysThePluginNeedsTheUpdate()
        => Assert.Equal(
            "Built for an older KHost (needs contracts 0.60.0; this KHost has 1.4.2); it needs an update.",
            ContractsVersion.Parse("1.4.2").DescribeRefusal(ContractsVersion.Parse("0.60.0")));

    [Fact]
    public void ThisHost_IsTheContractsVersionInDirectoryBuildProps()
    {
        var props = XDocument.Load(Path.Combine(RepositoryRoot(), "Directory.Build.props"));
        var declared = props.Descendants("ContractsVersion").Single().Value.Trim();

        Assert.Equal(declared, ContractsVersion.ThisHost.ToString());
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return directory!.FullName;
    }
}
