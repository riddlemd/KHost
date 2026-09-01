using KHost.Abstractions.Models.Plugins;
using KHost.Common.Plugins;

namespace KHost.UnitTests.Common.Plugins;

public class PluginVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, 0)]
    [InlineData("1.2.3.4", 1, 2, 3, 4)]
    [InlineData("1.2", 1, 2, 0, 0)]
    [InlineData("v2.0.1", 2, 0, 1, 0)]
    [InlineData("1.2.0-beta.4", 1, 2, 0, 0)]
    [InlineData("1.2.0+build9", 1, 2, 0, 0)]
    [InlineData("  1.4.0  ", 1, 4, 0, 0)]
    public void Parse_ReadableVersion_ReturnsComponents(string value, int major, int minor, int build, int revision)
        => Assert.Equal(new Version(major, minor, build, revision), PluginVersion.Parse(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    [InlineData("1.2.3.4.5")]
    public void Parse_UnreadableVersion_ReturnsZero(string? value)
        => Assert.Equal(new Version(0, 0, 0, 0), PluginVersion.Parse(value));

    [Fact]
    public void Parse_TrailingComponentsOmitted_EqualsExplicitZeroes()
        => Assert.Equal(PluginVersion.Parse("1.0.0.0"), PluginVersion.Parse("1.0"));

    [Theory]
    [InlineData("1.2.1", "1.2.0", true)]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData("1.1.0", "1.2.0", false)]
    [InlineData("1.2.0", null, true)]
    public void IsNewer_ComparesCandidateAgainstInstalled(string candidate, string? installed, bool expected)
        => Assert.Equal(expected, PluginVersion.IsNewer(candidate, installed));

    [Fact]
    public void IsNewer_UnreadableCandidate_IsNotNewer()
        => Assert.False(PluginVersion.IsNewer("latest", "1.0.0"));
}
