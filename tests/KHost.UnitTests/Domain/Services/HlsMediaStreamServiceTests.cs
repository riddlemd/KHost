using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using KHost.Domain.Services;

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
    public void BuildArguments_PairsGraphicsWithTheirCompanionAudio()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.cdg", TimeSpan.Zero, 0, 2, "/songs/a.mp3");

        Assert.Contains("-i \"/songs/a.cdg\"", arguments);
        Assert.Contains("-i \"/songs/a.mp3\"", arguments);

        // Without the mapping ffmpeg takes both streams from the first input, which has no audio.
        Assert.Contains("-map 0:v:0 -map 1:a:0", arguments);
    }

    [Fact]
    public void BuildArguments_SeeksOnTheOutput_ForAPairedSource()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.cdg", TimeSpan.FromSeconds(42), 0, 2, "/songs/a.mp3");

        // An input seek lands mid-packet and CDG decodes to garbage from there.
        var seek = arguments.IndexOf("-ss 42.000", StringComparison.Ordinal);
        var lastInput = arguments.LastIndexOf("-i \"", StringComparison.Ordinal);
        Assert.True(seek > lastInput, $"seek must follow both inputs: {arguments}");
    }

    [Fact]
    public void BuildArguments_KeepsTheFastInputSeek_ForAnOrdinaryFile()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.FromSeconds(42), 0, 2);

        Assert.True(
            arguments.IndexOf("-ss 42.000", StringComparison.Ordinal)
                < arguments.IndexOf("-i \"", StringComparison.Ordinal),
            arguments);
    }

    [Fact]
    public void BuildArguments_AddsAPitchFilter_OnlyWhenShifted()
    {
        Assert.DoesNotContain("asetrate", HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, 2));
        Assert.Contains("asetrate", HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 2, 2));
    }

    [Fact]
    public void BuildArguments_ForcesKeyframesOnTime_NotOnAFrameCount()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, 2);

        // -g is frames, so it equals the segment length at one source frame rate only, and the
        // muxer cuts only where a keyframe already is.
        Assert.Contains("-force_key_frames \"expr:gte(t,n_forced*2)\"", arguments);
        Assert.DoesNotContain("-g 60", arguments);
        Assert.DoesNotContain("-keyint_min", arguments);
    }

    [Fact]
    public void BuildArguments_TiesForcedKeyframesToTheSegmentLength()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, 4);

        // A keyframe interval disagreeing with hls_time gives ragged segments.
        Assert.Contains("-force_key_frames \"expr:gte(t,n_forced*4)\"", arguments);
        Assert.Contains("-hls_time 4", arguments);
    }

    [Fact]
    public void BuildArguments_ResamplesBeforeReinterpretingTheSampleRate()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 2, 2);

        // asetrate reinterprets whatever rate reaches it, and the atempo below compensates only
        // for the intended ratio — so a 48kHz source drifts off the video without this.
        Assert.Contains("aresample=44100,asetrate=", arguments);
        Assert.True(
            arguments.IndexOf("aresample=44100,asetrate=", StringComparison.Ordinal)
                < arguments.IndexOf("atempo=", StringComparison.Ordinal),
            arguments);
    }

    [Theory]
    [InlineData(-6)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(6)]
    public void BuildArguments_KeepsTheCompensatingTempoWithinWhatAtempoAccepts(int semitones)
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, semitones, 2);

        var start = arguments.IndexOf("atempo=", StringComparison.Ordinal) + "atempo=".Length;
        var end = arguments.IndexOf('"', start);
        var tempo = double.Parse(arguments[start..end], CultureInfo.InvariantCulture);

        // atempo rejects anything under 0.5, and chaining is the only way past it. Across the
        // supported range pitch alone never gets there — combining it with a tempo change would.
        Assert.InRange(tempo, 0.5, 100.0);
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
