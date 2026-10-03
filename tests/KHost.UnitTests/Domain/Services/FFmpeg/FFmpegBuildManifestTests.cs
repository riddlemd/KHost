using System.Text.RegularExpressions;
using KHost.Domain.Services.FFmpeg;

namespace KHost.UnitTests.Domain.Services.FFmpeg;

/// <summary>The manifest is the trust root for what the host downloads and runs, so its shape is
/// checked here the way the plugin catalog's is.</summary>
public partial class FFmpegBuildManifestTests
{
    private static readonly string[] KnownRids = ["win-x64", "win-arm64", "macos-x64", "macos-arm64", "linux-x64", "linux-arm64"];

    public static TheoryData<string> Rids => [.. FFmpegBuildManifest.Embedded.Builds.Select(b => b.Rid)];

    [Fact]
    public void Embedded_Loads_AndPinsTheOwnersMachines()
    {
        var manifest = FFmpegBuildManifest.Embedded;

        Assert.Equal(FFmpegBuildManifest.SupportedSchemaVersion, manifest.SchemaVersion);
        Assert.NotNull(manifest.SelectFor("win", "x64"));
        Assert.NotNull(manifest.SelectFor("macos", "arm64"));
    }

    [Theory]
    [MemberData(nameof(Rids))]
    public void Embedded_EveryDownloadIsHttpsAndHashPinned(string rid)
    {
        var build = FFmpegBuildManifest.Embedded.Builds.Single(b => b.Rid == rid);

        Assert.Contains(rid, KnownRids);
        Assert.False(string.IsNullOrWhiteSpace(build.Version));
        Assert.False(string.IsNullOrWhiteSpace(build.Licence));
        Assert.NotEmpty(build.Downloads);

        foreach (var download in build.Downloads)
        {
            Assert.StartsWith("https://", download.Url);
            Assert.Matches(Sha256(), download.Sha256);
            Assert.True(download.Size > 0, $"{download.Url} has no pinned size.");
        }
    }

    [Theory]
    [MemberData(nameof(Rids))]
    public void Embedded_EveryBuildCarriesBothPrograms(string rid)
    {
        var names = FFmpegBuildManifest.Embedded.Builds.Single(b => b.Rid == rid)
            .Downloads.SelectMany(d => d.Files.Keys).Order().ToArray();

        Assert.Equal(["ffmpeg", "ffprobe"], names);
    }

    /// <summary>A URL that moves with every release is not a pin; the builds carry a version.</summary>
    [Theory]
    [MemberData(nameof(Rids))]
    public void Embedded_NoUrlNamesAMovingLatest(string rid)
    {
        foreach (var download in FFmpegBuildManifest.Embedded.Builds.Single(b => b.Rid == rid).Downloads)
            Assert.DoesNotContain("latest", download.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("macos", "arm64", "macos-arm64")]
    [InlineData("macos", "x64", "macos-x64")]
    [InlineData("win", "x64", "win-x64")]
    public void SelectFor_ReturnsTheBuildForExactlyThatPlatform(string platform, string architecture, string rid)
        => Assert.Equal(rid, FFmpegBuildManifest.Embedded.SelectFor(platform, architecture)?.Rid);

    [Theory]
    [InlineData("osx", "arm64")]
    [InlineData("osx", "x64")]
    [InlineData("win", "arm64")]
    [InlineData("linux", "x64")]
    [InlineData("", "")]
    public void SelectFor_NothingPinned_IsNull(string platform, string architecture)
        => Assert.Null(FFmpegBuildManifest.Embedded.SelectFor(platform, architecture));

    [Fact]
    public void Parse_UnknownSchema_IsRefusedWhole()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => FFmpegBuildManifest.Parse("""{ "schemaVersion": 2, "builds": [] }"""));

        Assert.Contains("schema", error.Message);
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256();
}
