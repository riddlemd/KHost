using System.Runtime.InteropServices;
using KHost.Domain.Services.VideoEncoding;
using Microsoft.Extensions.Logging;

namespace KHost.UnitTests.Domain.Services.VideoEncoding;

/// <summary>Which encoder a song is cut with: the first hardware one that really encodes here,
/// else libx264, decided once per process.</summary>
public class VideoEncoderSelectorTests
{
    private readonly FakeRunner _runner = new();
    private readonly RecordingLogger _logger = new();

    [Fact]
    public void CandidatesFor_TriesEachPlatformsEncodersInOrder()
    {
        Assert.Equal(["h264_videotoolbox"], Codecs(VideoEncoderSelector.CandidatesFor(OSPlatform.OSX)));
        Assert.Equal(
            ["h264_qsv", "h264_amf", "h264_nvenc", "h264_mf"],
            Codecs(VideoEncoderSelector.CandidatesFor(OSPlatform.Windows)));
        Assert.Equal(["h264_nvenc", "h264_qsv"], Codecs(VideoEncoderSelector.CandidatesFor(OSPlatform.Linux)));
    }

    [Fact]
    public async Task SelectAsync_Auto_TakesTheFirstCandidateThatWorks()
    {
        _runner.Listed("h264_qsv", "h264_amf", "h264_nvenc");
        _runner.Outcomes["h264_qsv"] = Outcome.Fails;
        _runner.Outcomes["h264_amf"] = Outcome.Works;
        _runner.Outcomes["h264_nvenc"] = Outcome.Works;

        var chosen = await Selector(Windows).SelectAsync(VideoEncoderPreference.Auto);

        Assert.Equal(VideoEncoderProfile.Amf, chosen);
        // Stopped at the first that worked; nvenc was never run.
        Assert.Equal(["h264_qsv", "h264_amf"], _runner.Probed);
        Assert.True(_logger.Has(LogLevel.Information, "h264_qsv: failed the test encode (Cannot load nvcuda)"));
        Assert.True(_logger.Has(LogLevel.Information, "h264_amf: works"));
    }

    [Fact]
    public async Task SelectAsync_SkipsAnEncoderThisBuildDoesNotList_WithoutRunningIt()
    {
        _runner.Listed("h264_amf");
        _runner.Outcomes["h264_qsv"] = Outcome.Works;
        _runner.Outcomes["h264_amf"] = Outcome.Works;

        var chosen = await Selector(Windows).SelectAsync(VideoEncoderPreference.Auto);

        Assert.Equal(VideoEncoderProfile.Amf, chosen);
        Assert.Equal(["h264_amf"], _runner.Probed);
        Assert.True(_logger.Has(LogLevel.Information, "h264_qsv: not in this ffmpeg build"));
    }

    /// <summary>An encoder that ignores the forced keyframes cuts one long segment, which a player
    /// would stall on; it does not count as working.</summary>
    [Fact]
    public async Task SelectAsync_RejectsAnEncoderWhoseKeyframesMissTheCadence()
    {
        _runner.Listed("h264_videotoolbox");
        _runner.Outcomes["h264_videotoolbox"] = Outcome.OffCadence;

        var chosen = await Selector([VideoEncoderProfile.VideoToolbox]).SelectAsync(VideoEncoderPreference.Auto);

        Assert.Equal(VideoEncoderProfile.Software, chosen);
        Assert.True(_logger.Has(LogLevel.Information, "keyframes missed the segment cadence (segments 3.00)"));
    }

    [Fact]
    public async Task SelectAsync_NoneWorks_FallsBackToLibx264()
    {
        _runner.Listed("h264_qsv", "h264_amf", "h264_nvenc", "h264_mf");

        var chosen = await Selector(Windows).SelectAsync(VideoEncoderPreference.Auto);

        Assert.Equal(VideoEncoderProfile.Software, chosen);
        Assert.Equal(4, _runner.Probed.Count);
        Assert.True(_logger.Has(LogLevel.Information, "uses libx264: no hardware encoder works here"));
        Assert.False(_logger.Has(LogLevel.Warning, "Hardware video encoding was asked for"));
    }

    [Fact]
    public async Task SelectAsync_Software_UsesLibx264WithoutProbing()
    {
        _runner.Listed("h264_videotoolbox");
        _runner.Outcomes["h264_videotoolbox"] = Outcome.Works;

        var chosen = await Selector([VideoEncoderProfile.VideoToolbox]).SelectAsync(VideoEncoderPreference.Software);

        Assert.Equal(VideoEncoderProfile.Software, chosen);
        Assert.Equal(0, _runner.Runs);
    }

    [Fact]
    public async Task SelectAsync_HardwareButNoneWorks_WarnsOnceAndFallsBack()
    {
        _runner.Listed("h264_videotoolbox");
        var selector = Selector([VideoEncoderProfile.VideoToolbox]);

        Assert.Equal(VideoEncoderProfile.Software, await selector.SelectAsync(VideoEncoderPreference.Hardware));
        Assert.Equal(VideoEncoderProfile.Software, await selector.SelectAsync(VideoEncoderPreference.Hardware));

        Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Hardware video encoding was asked for"));
    }

    [Fact]
    public async Task SelectAsync_ProbesOncePerProcess()
    {
        _runner.Listed("h264_videotoolbox");
        _runner.Outcomes["h264_videotoolbox"] = Outcome.Works;
        var selector = Selector([VideoEncoderProfile.VideoToolbox]);

        await selector.SelectAsync(VideoEncoderPreference.Auto);
        await selector.SelectAsync(VideoEncoderPreference.Hardware);

        Assert.Equal(2, _runner.Runs);
    }

    [Fact]
    public async Task ReportFailure_KeepsTheFailedEncoderOffForTheRestOfTheProcess()
    {
        _runner.Listed("h264_videotoolbox");
        _runner.Outcomes["h264_videotoolbox"] = Outcome.Works;
        var selector = Selector([VideoEncoderProfile.VideoToolbox]);

        Assert.Equal(VideoEncoderProfile.VideoToolbox, await selector.SelectAsync(VideoEncoderPreference.Auto));

        selector.ReportFailure(VideoEncoderProfile.VideoToolbox);

        Assert.Equal(VideoEncoderProfile.Software, await selector.SelectAsync(VideoEncoderPreference.Auto));
        Assert.True(_logger.Has(LogLevel.Warning, "h264_videotoolbox failed a song"));
    }

    private static readonly IReadOnlyList<VideoEncoderProfile> Windows = VideoEncoderSelector.CandidatesFor(OSPlatform.Windows);

    private VideoEncoderSelector Selector(IReadOnlyList<VideoEncoderProfile> candidates) => new(_logger, _runner, candidates);

    private static string[] Codecs(IEnumerable<VideoEncoderProfile> profiles) => [.. profiles.Select(p => p.Codec)];

    private enum Outcome { Fails, Works, OffCadence }

    /// <summary>Answers the encoder listing, and "encodes" by writing the playlist a real run would.</summary>
    private sealed class FakeRunner : IFfmpegProcessRunner
    {
        private string _listing = "";

        public Dictionary<string, Outcome> Outcomes { get; } = [];
        public List<string> Probed { get; } = [];
        public int Runs { get; private set; }

        public void Listed(params string[] codecs)
            => _listing = "Encoders:\n ------\n V....D libx264              libx264 H.264\n"
                          + string.Concat(codecs.Select(c => $" V....D {c,-20} {c} H.264\n"))
                          // A substring of a listed name must not count as listed.
                          + " V....D h264_qsv_like        not the one\n";

        public async Task<FfmpegRun> RunAsync(
            string arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Runs++;

            if (arguments.Contains("-encoders")) return new FfmpegRun(0, _listing, "");

            var codec = arguments.Split(' ').SkipWhile(a => a != "-c:v").Skip(1).First();
            Probed.Add(codec);

            switch (Outcomes.GetValueOrDefault(codec, Outcome.Fails))
            {
                case Outcome.Works:
                    await File.WriteAllTextAsync(Path.Combine(workingDirectory, "probe.m3u8"),
                        "#EXTM3U\n#EXTINF:1.000000,\nprobe_000.ts\n#EXTINF:1.000000,\nprobe_001.ts\n#EXTINF:1.000000,\nprobe_002.ts\n", cancellationToken);
                    return new FfmpegRun(0, "", "");
                case Outcome.OffCadence:
                    await File.WriteAllTextAsync(Path.Combine(workingDirectory, "probe.m3u8"),
                        "#EXTM3U\n#EXTINF:3.000000,\nprobe_000.ts\n", cancellationToken);
                    return new FfmpegRun(0, "", "");
                default:
                    return new FfmpegRun(1, "", "\nCannot load nvcuda\nmore detail\n");
            }
        }
    }

    private sealed class RecordingLogger : ILogger<VideoEncoderSelector>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        public bool Has(LogLevel level, string containing)
            => Entries.Any(e => e.Level == level && e.Message.Contains(containing));
    }
}
