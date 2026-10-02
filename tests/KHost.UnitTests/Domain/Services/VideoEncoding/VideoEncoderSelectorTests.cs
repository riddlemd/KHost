using System.Runtime.InteropServices;
using KHost.Abstractions.Messaging.Messages;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.VideoEncoding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services.VideoEncoding;

/// <summary>Which encoder a song is cut with: the first hardware one that really encodes here,
/// else libx264, decided once per process.</summary>
public class VideoEncoderSelectorTests
{
    private readonly FakeRunner _runner = new();
    private readonly RecordingLogger _logger = new();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

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

    /// <summary>With no ffmpeg the song fails on its own; caching a probe then would keep libx264
    /// after the install that fixes it, and the probe runs the copy the host found.</summary>
    [Fact]
    public async Task SelectAsync_NoFfmpegYet_ProbesNothingUntilOneIsInstalled()
    {
        _runner.Listed("h264_videotoolbox");
        _runner.Outcomes["h264_videotoolbox"] = Outcome.Works;
        _runner.Located = null;
        var selector = Selector([VideoEncoderProfile.VideoToolbox]);

        Assert.Equal(VideoEncoderProfile.Software, await selector.SelectAsync(VideoEncoderPreference.Auto));
        Assert.Equal(0, _runner.Runs);

        _runner.Located = "/host/bin/ffmpeg";

        Assert.Equal(VideoEncoderProfile.VideoToolbox, await selector.SelectAsync(VideoEncoderPreference.Auto));
        Assert.All(_runner.RanWith, path => Assert.Equal("/host/bin/ffmpeg", path));
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

    /// <summary>A song that opens while the startup probe is still running waits for it rather than
    /// starting a second one beside it.</summary>
    [Fact]
    public async Task WarmAsync_ASongOpensMidProbe_WaitsForTheSameProbe()
    {
        _runner.Listed("h264_videotoolbox");
        _runner.Outcomes["h264_videotoolbox"] = Outcome.Works;
        _runner.ListingGate = new TaskCompletionSource();
        var selector = Selector([VideoEncoderProfile.VideoToolbox]);

        var warm = selector.WarmAsync(VideoEncoderPreference.Auto);
        var song = selector.SelectAsync(VideoEncoderPreference.Auto);

        Assert.False(song.IsCompleted);
        _runner.ListingGate.SetResult();
        await warm;

        Assert.Equal(VideoEncoderProfile.VideoToolbox, await song);
        Assert.Equal(1, _runner.Listings);
        Assert.Equal(["h264_videotoolbox"], _runner.Probed);
    }

    [Fact]
    public async Task SelectAsync_ConcurrentOpens_ShareOneProbe()
    {
        _runner.Listed("h264_videotoolbox");
        _runner.Outcomes["h264_videotoolbox"] = Outcome.Works;
        _runner.ListingGate = new TaskCompletionSource();
        var selector = Selector([VideoEncoderProfile.VideoToolbox]);

        var songs = Enumerable.Range(0, 4).Select(_ => selector.SelectAsync(VideoEncoderPreference.Auto)).ToList();
        _runner.ListingGate.SetResult();

        Assert.All(await Task.WhenAll(songs), chosen => Assert.Equal(VideoEncoderProfile.VideoToolbox, chosen));
        Assert.Equal(1, _runner.Listings);
        Assert.Equal(["h264_videotoolbox"], _runner.Probed);
    }

    /// <summary>A new FFmpeg directory or an install is probed in the background, not by the next song.</summary>
    [Fact]
    public async Task FFmpegChanged_ToAnotherFfmpeg_ProbesItBeforeASongAsks()
    {
        _runner.Listed("h264_videotoolbox");
        _runner.Outcomes["h264_videotoolbox"] = Outcome.Works;
        var selector = Selector([VideoEncoderProfile.VideoToolbox]);
        await selector.SelectAsync(VideoEncoderPreference.Auto);

        _runner.Located = "/host/bin/ffmpeg";
        await _broker.PublishAsync(new FFmpegChanged());
        await WaitUntilAsync(() => _runner.Probed.Count == 2);
        var runs = _runner.Runs;

        Assert.Equal(VideoEncoderProfile.VideoToolbox, await selector.SelectAsync(VideoEncoderPreference.Auto));
        Assert.Equal(runs, _runner.Runs);
        Assert.Contains("/host/bin/ffmpeg", _runner.RanWith);
    }

    /// <summary>The same path is not the same program once an install has replaced the file.</summary>
    [Fact]
    public async Task FFmpegChanged_ReinstalledOverTheSamePath_ProbesAgain()
    {
        var ffmpeg = Path.Combine(Path.GetTempPath(), $"khost-selector-{Guid.NewGuid():n}");
        await File.WriteAllTextAsync(ffmpeg, "old build");
        File.SetLastWriteTimeUtc(ffmpeg, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        try
        {
            _runner.Located = ffmpeg;
            _runner.Listed("h264_videotoolbox");
            _runner.Outcomes["h264_videotoolbox"] = Outcome.Works;
            var selector = Selector([VideoEncoderProfile.VideoToolbox]);
            await selector.SelectAsync(VideoEncoderPreference.Auto);

            File.SetLastWriteTimeUtc(ffmpeg, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
            await _broker.PublishAsync(new FFmpegChanged());

            await WaitUntilAsync(() => _runner.Listings == 2);
        }
        finally
        {
            File.Delete(ffmpeg);
        }
    }

    /// <summary>Announced for every percent of an install's download; none of those is a new program.</summary>
    [Fact]
    public async Task FFmpegChanged_SameFfmpeg_ProbesNothingMore()
    {
        _runner.Listed("h264_videotoolbox");
        _runner.Outcomes["h264_videotoolbox"] = Outcome.Works;
        var selector = Selector([VideoEncoderProfile.VideoToolbox]);
        await selector.SelectAsync(VideoEncoderPreference.Auto);

        await _broker.PublishAsync(new FFmpegChanged());
        await Task.Delay(50);

        Assert.Equal(2, _runner.Runs);
    }

    /// <summary>A host who chose libx264 never has a GPU driver opened behind their back.</summary>
    [Fact]
    public async Task FFmpegChanged_BeforeHardwareWasEverWanted_ProbesNothing()
    {
        _runner.Listed("h264_videotoolbox");
        var selector = Selector([VideoEncoderProfile.VideoToolbox]);
        await selector.WarmAsync(VideoEncoderPreference.Software);

        await _broker.PublishAsync(new FFmpegChanged());
        await Task.Delay(50);

        Assert.Equal(0, _runner.Runs);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);

        Assert.True(condition(), "the condition never held");
    }

    private static readonly IReadOnlyList<VideoEncoderProfile> Windows = VideoEncoderSelector.CandidatesFor(OSPlatform.Windows);

    private VideoEncoderSelector Selector(IReadOnlyList<VideoEncoderProfile> candidates)
        => new(_logger, _runner, _broker, candidates);

    private static string[] Codecs(IEnumerable<VideoEncoderProfile> profiles) => [.. profiles.Select(p => p.Codec)];

    private enum Outcome { Fails, Works, OffCadence }

    /// <summary>Answers the encoder listing, and "encodes" by writing the playlist a real run would.</summary>
    private sealed class FakeRunner : IFfmpegProcessRunner
    {
        private string _listing = "";

        private int _runs;
        private int _listings;

        public Dictionary<string, Outcome> Outcomes { get; } = [];
        public List<string> Probed { get; } = [];
        public int Runs => Volatile.Read(ref _runs);
        public int Listings => Volatile.Read(ref _listings);

        /// <summary>Holds the encoder listing until released, so a probe can be caught mid-run.</summary>
        public TaskCompletionSource? ListingGate { get; set; }

        /// <summary>The ffmpeg the host would run; null while none is installed.</summary>
        public string? Located { get; set; } = "/opt/ffmpeg/ffmpeg";

        public List<string> RanWith { get; } = [];

        public string? Locate() => Located;

        public void Listed(params string[] codecs)
            => _listing = "Encoders:\n ------\n V....D libx264              libx264 H.264\n"
                          + string.Concat(codecs.Select(c => $" V....D {c,-20} {c} H.264\n"))
                          // A substring of a listed name must not count as listed.
                          + " V....D h264_qsv_like        not the one\n";

        public async Task<FfmpegRun> RunAsync(
            string ffmpegPath, string arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _runs);
            lock (RanWith) RanWith.Add(ffmpegPath);

            if (arguments.Contains("-encoders"))
            {
                Interlocked.Increment(ref _listings);
                if (ListingGate is { } gate) await gate.Task;
                return new FfmpegRun(0, _listing, "");
            }

            var codec = arguments.Split(' ').SkipWhile(a => a != "-c:v").Skip(1).First();
            lock (Probed) Probed.Add(codec);

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
