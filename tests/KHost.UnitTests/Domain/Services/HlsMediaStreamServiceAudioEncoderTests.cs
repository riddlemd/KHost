using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.VideoEncoding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

/// <summary>Audio alone names no encoder and always runs software, whatever the selector answers.</summary>
/// <remarks>ffmpeg is found nowhere, so the run stops right after its command line is logged; a
/// hardware run is the one that leaves its playlist open (<c>omit_endlist</c>).</remarks>
public sealed class HlsMediaStreamServiceAudioEncoderTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("khost-audio-encoder-");
    private readonly IFFmpegService _ffmpeg = Substitute.For<IFFmpegService>();
    private readonly CommandLineLogger _logger = new();
    private readonly HlsMediaStreamService _service;

    public HlsMediaStreamServiceAudioEncoderTests()
    {
        _ffmpeg.Locate(FFmpegTool.FFmpeg).Returns((string?)null);

        _service = new HlsMediaStreamService(
            _logger,
            new TestOptionsMonitor<HlsMediaStreamService.ServiceOptions>(new HlsMediaStreamService.ServiceOptions
            {
                WorkingDirectory = Path.Combine(_root.FullName, "streams"),
            }),
            new PlayableMediaSourceService(NullLogger<PlayableMediaSourceService>.Instance, []),
            TimeProvider.System,
            _ffmpeg,
            new HardwareEncoderSelector());
    }

    public void Dispose()
    {
        _service.Dispose();

        try { _root.Delete(recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task OpenUnderDrawnWordsAsync_AudioSource_RunsSoftwareThoughHardwareIsChosen()
    {
        var song = Song("song.mp3");

        await Assert.ThrowsAsync<KHostException>(() => _service.OpenUnderDrawnWordsAsync(song, TimeSpan.Zero, 0, 0, null));

        Assert.DoesNotContain("omit_endlist", Assert.Single(_logger.CommandLines));
    }

    [Fact]
    public async Task OpenStemsAsync_AudioAlone_RunsSoftwareThoughHardwareIsChosen()
    {
        await Assert.ThrowsAsync<KHostException>(() => _service.OpenStemsAsync(
            "/songs/a.kit", [new StemSource(0, AudioTrackRole.Music, "http://stems.test/music.ogg", 100)],
            TimeSpan.Zero, 0, 0, words: null, adopt: null));

        Assert.DoesNotContain("omit_endlist", Assert.Single(_logger.CommandLines));
    }

    /// <summary>The control: the same open with a picture does take the hardware encoder.</summary>
    [Fact]
    public async Task OpenUnderDrawnWordsAsync_VideoSource_RunsTheChosenHardware()
    {
        var song = Song("song.mp4");

        await Assert.ThrowsAsync<KHostException>(() => _service.OpenUnderDrawnWordsAsync(song, TimeSpan.Zero, 0, 0, null));

        var commandLine = Assert.Single(_logger.CommandLines);
        Assert.Contains("omit_endlist", commandLine);
        Assert.Contains(VideoEncoderProfile.VideoToolbox.Codec, commandLine);
    }

    private string Song(string name)
    {
        var path = Path.Combine(_root.FullName, name);
        File.WriteAllText(path, "not really media");
        return path;
    }

    private sealed class HardwareEncoderSelector : IVideoEncoderSelector
    {
        public Task WarmAsync(VideoEncoderPreference preference) => Task.CompletedTask;

        public Task<VideoEncoderProfile> SelectAsync(VideoEncoderPreference preference, CancellationToken cancellationToken = default)
            => Task.FromResult(VideoEncoderProfile.VideoToolbox);

        public void ReportFailure(VideoEncoderProfile encoder) { }
    }

    /// <summary>Keeps the ffmpeg command lines the service logs before starting each run.</summary>
    private sealed class CommandLineLogger : ILogger<HlsMediaStreamService>
    {
        private readonly List<string> _commandLines = [];

        public IReadOnlyList<string> CommandLines { get { lock (_commandLines) return [.. _commandLines]; } }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is IReadOnlyList<KeyValuePair<string, object?>> fields
                && fields.Any(field => field is { Key: "{OriginalFormat}", Value: "ffmpeg {Arguments}" }))
            {
                lock (_commandLines) _commandLines.Add(formatter(state, exception));
            }
        }
    }
}
