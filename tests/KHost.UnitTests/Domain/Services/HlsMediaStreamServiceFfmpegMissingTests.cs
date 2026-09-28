using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

/// <summary>A missing ffmpeg used to reach the room as a bare Win32Exception on the first song.</summary>
public sealed class HlsMediaStreamServiceFfmpegMissingTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("khost-ffmpeg-missing-");
    private readonly IFFmpegService _ffmpeg = Substitute.For<IFFmpegService>();
    private readonly HlsMediaStreamService _service;
    private readonly string _song;

    public HlsMediaStreamServiceFfmpegMissingTests()
    {
        _song = Path.Combine(_root.FullName, "song.mp4");
        File.WriteAllText(_song, "not really a video");

        _service = new HlsMediaStreamService(
            NullLogger<HlsMediaStreamService>.Instance,
            new TestOptionsMonitor<HlsMediaStreamService.ServiceOptions>(new HlsMediaStreamService.ServiceOptions
            {
                WorkingDirectory = Path.Combine(_root.FullName, "streams"),
            }),
            new PlayableMediaSourceService(NullLogger<PlayableMediaSourceService>.Instance, []),
            _ffmpeg);
    }

    public void Dispose()
    {
        _service.Dispose();

        try { _root.Delete(recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task OpenAsync_FfmpegFoundNowhere_ThrowsTheCodeThatSaysHowToFixIt()
    {
        _ffmpeg.Locate(FFmpegTool.FFmpeg).Returns((string?)null);

        var error = await Assert.ThrowsAsync<KHostException>(() => _service.OpenAsync(_song));

        Assert.Equal("KH-FFMPEG-MISSING", error.ReferenceCode);
        Assert.Contains("Install FFmpeg", error.Suggestion);
    }

    /// <summary>Found by name but not startable: a file that is not a program, or not executable.</summary>
    [Fact]
    public async Task OpenAsync_FfmpegCannotBeStarted_ThrowsTheSameCode()
    {
        var notAProgram = Path.Combine(_root.FullName, "ffmpeg");
        File.WriteAllText(notAProgram, "not a program");
        _ffmpeg.Locate(FFmpegTool.FFmpeg).Returns(notAProgram);

        var error = await Assert.ThrowsAsync<KHostException>(() => _service.OpenAsync(_song));

        Assert.Equal("KH-FFMPEG-MISSING", error.ReferenceCode);
    }

    [Fact]
    public async Task OpenAsync_FfmpegMissing_LeavesNoSessionFolderBehind()
    {
        _ffmpeg.Locate(FFmpegTool.FFmpeg).Returns((string?)null);

        await Assert.ThrowsAsync<KHostException>(() => _service.OpenAsync(_song));

        Assert.Empty(Directory.GetDirectories(Path.Combine(_root.FullName, "streams")));
    }
}
