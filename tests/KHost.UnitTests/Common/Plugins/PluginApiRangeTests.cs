using KHost.Abstractions.Models.Plugins;
using KHost.Common.Plugins;

namespace KHost.UnitTests.Common.Plugins;

public class PluginApiRangeTests
{
    private static readonly PluginApiRange Range = new(Minimum: 3, Current: 5);

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Covers_InsideTheRangeEndsIncluded_IsTrue(int apiVersion)
        => Assert.True(Range.Covers(apiVersion));

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    public void Covers_OutsideTheRange_IsFalse(int apiVersion)
        => Assert.False(Range.Covers(apiVersion));

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void DescribeRefusal_InsideTheRange_IsNull(int apiVersion)
        => Assert.Null(Range.DescribeRefusal(apiVersion));

    [Fact]
    public void DescribeRefusal_NewerThanCurrent_SaysKHostNeedsTheUpdate()
        => Assert.Equal(
            "Needs a newer KHost (built for plugin API 6; this KHost runs 3–5).",
            Range.DescribeRefusal(6));

    [Fact]
    public void DescribeRefusal_OlderThanMinimum_SaysThePluginNeedsTheUpdate()
        => Assert.Equal(
            "Built for an older KHost (plugin API 2; this KHost runs 3–5); it needs an update.",
            Range.DescribeRefusal(2));

    [Fact]
    public void DescribeRefusal_RangeOfOneVersion_NamesItOnce()
        => Assert.Equal(
            "Needs a newer KHost (built for plugin API 8; this KHost runs 7).",
            new PluginApiRange(7, 7).DescribeRefusal(8));

    [Fact]
    public void ThisHost_IsTheDeclaredConstants()
    {
        Assert.Equal(PluginApi.MinimumVersion, PluginApiRange.ThisHost.Minimum);
        Assert.Equal(PluginApi.CurrentVersion, PluginApiRange.ThisHost.Current);
    }
}
