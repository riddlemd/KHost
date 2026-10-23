using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.Displays;
using KHost.Domain.Services.VideoEncoding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

/// <summary>Each open asks what the connected display decodes: one with no H.264 gets VP9, never the
/// selector's hardware H.264.</summary>
/// <remarks>ffmpeg is found nowhere, so the run stops right after its command line is logged.</remarks>
public sealed class HlsMediaStreamServiceDisplayCodecsTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("khost-display-codecs-");
    private readonly IFFmpegService _ffmpeg = Substitute.For<IFFmpegService>();
    private readonly IDisplayCodecs _codecs = Substitute.For<IDisplayCodecs>();
    private readonly CommandLineLogger _logger = new();
    private readonly HlsMediaStreamService _service;

    public HlsMediaStreamServiceDisplayCodecsTests()
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
            new HardwareEncoderSelector(),
            _codecs);
    }

    public void Dispose()
    {
        _service.Dispose();

        try { _root.Delete(recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task OpenAsync_DisplayDecodesOnlyOpenCodecs_CutsVp9NotTheChosenHardware()
    {
        _codecs.OpenCodecsOnly.Returns(true);

        await Assert.ThrowsAsync<KHostException>(() => _service.OpenAsync(Song("song.mp4"), TimeSpan.Zero, 0, 0, null));

        var line = Assert.Single(_logger.CommandLines);
        Assert.Contains("-c:v libvpx-vp9", line);
        Assert.Contains("-c:a libopus", line);
        Assert.DoesNotContain(VideoEncoderProfile.VideoToolbox.Codec, line);
    }

    [Fact]
    public async Task OpenAsync_DisplayDecodesH264_RunsTheChosenHardware()
    {
        _codecs.OpenCodecsOnly.Returns(false);

        await Assert.ThrowsAsync<KHostException>(() => _service.OpenAsync(Song("song.mp4"), TimeSpan.Zero, 0, 0, null));

        var line = Assert.Single(_logger.CommandLines);
        Assert.Contains(VideoEncoderProfile.VideoToolbox.Codec, line);
        Assert.Contains("-c:a aac", line);
    }

    /// <summary>A song's audio alone (break music, an ad) is Opus too: AAC would not play either.</summary>
    [Fact]
    public async Task OpenAsync_AudioAlone_DisplayDecodesOnlyOpenCodecs_IsOpus()
    {
        _codecs.OpenCodecsOnly.Returns(true);

        await Assert.ThrowsAsync<KHostException>(
            () => _service.OpenUnderDrawnWordsAsync(Song("song.mp3"), TimeSpan.Zero, 0, 0, null));

        var line = Assert.Single(_logger.CommandLines);
        Assert.Contains("-c:a libopus", line);
        Assert.DoesNotContain("-c:v", line);
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
