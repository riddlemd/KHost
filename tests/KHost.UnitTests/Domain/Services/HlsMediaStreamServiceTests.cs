using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services;

public class HlsMediaStreamServiceTests : IDisposable
{
    private readonly string _workingDirectory =
        Path.Combine(Path.GetTempPath(), $"khost-stream-tests-{Guid.NewGuid():n}");

    private readonly HlsMediaStreamService _service;

    public HlsMediaStreamServiceTests()
        => _service = new HlsMediaStreamService(
            NullLogger<HlsMediaStreamService>.Instance,
            Options.Create(new HlsMediaStreamService.ServiceOptions
            {
                BaseAddress = "http://host:5251",
                WorkingDirectory = _workingDirectory,
            }));

    [Fact]
    public void BuildArguments_TargetsCodecsEveryConsumerDecodes()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, 2);

        // The intersection of Chromecast, WKWebView and browser support. Widening any of these
        // silently drops one class of consumer.
        Assert.Contains("-c:v libx264", arguments);
        Assert.Contains("-profile:v main", arguments);
        Assert.Contains("-level 4.1", arguments);
        Assert.Contains("-c:a aac", arguments);
        Assert.Contains("-ar 44100", arguments);
    }

    [Fact]
    public void BuildArguments_SegmentsAsMpegTs_NotFragmentedMp4()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, 2);

        // CMAF/fMP4 needs a newer Cast receiver than TS does.
        Assert.Contains("-f hls", arguments);
        Assert.Contains("seg_%05d.ts", arguments);
    }

    [Fact]
    public void BuildArguments_PutsSeekBeforeTheInput()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.FromSeconds(42), 0, 2);

        // Input-side -ss is the fast one; after -i ffmpeg decodes everything it skips.
        Assert.True(arguments.IndexOf("-ss 42.000", StringComparison.Ordinal)
                    < arguments.IndexOf("-i ", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildArguments_OmitsTheSeek_WhenStartingAtZero()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, 2);

        Assert.DoesNotContain("-ss ", arguments);
    }

    [Fact]
    public void BuildArguments_AddsAPitchFilter_OnlyWhenShifted()
    {
        Assert.DoesNotContain("asetrate", HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, 2));
        Assert.Contains("asetrate", HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 2, 2));
    }

    [Fact]
    public async Task OpenAsync_ThrowsForAMissingFile()
        => await Assert.ThrowsAsync<FileNotFoundException>(
            () => _service.OpenAsync(Path.Combine(_workingDirectory, "nope.mp4")));

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..\\..\\secrets.txt")]
    [InlineData("sub/dir.ts")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    public void ResolveArtifact_RejectsAnythingThatIsNotABareFileName(string fileName)
        => Assert.Null(_service.ResolveArtifact("any-session", fileName));

    [Fact]
    public void ResolveArtifact_ReturnsNullForAnUnknownSession()
        => Assert.Null(_service.ResolveArtifact("no-such-session", "stream.m3u8"));

    public void Dispose()
    {
        _service.Dispose();
        try { Directory.Delete(_workingDirectory, recursive: true); } catch { /* scratch */ }
        GC.SuppressFinalize(this);
    }
}
