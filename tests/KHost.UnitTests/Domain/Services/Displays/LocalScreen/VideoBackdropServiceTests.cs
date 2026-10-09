using KHost.Abstractions.Models;
using KHost.Domain.Services;
using KHost.Domain.Services.Displays.LocalScreen;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace KHost.UnitTests.Domain.Services.Displays.LocalScreen;

/// <summary>A library video is served as it is when the screen plays it, else encoded once per run
/// and file, and only through tokens the service handed out.</summary>
public class VideoBackdropServiceTests : IDisposable
{
    private static readonly VideoBackdropFacts Direct = new("mov,mp4,m4a,3gp,3g2,mj2", "h264", "yuv420p", "aac");
    private static readonly VideoBackdropFacts NeedsEncoding = new("matroska,webm", "vp9", "yuv420p", "opus");

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"khost-backdrop-tests-{Guid.NewGuid():n}");
    private readonly IVideoBackdropProbe _probe = Substitute.For<IVideoBackdropProbe>();
    private readonly IVideoBackdropEncoder _encoder = Substitute.For<IVideoBackdropEncoder>();
    private readonly IOptionsMonitor<HlsMediaStreamService.ServiceOptions> _options = Substitute.For<IOptionsMonitor<HlsMediaStreamService.ServiceOptions>>();
    private readonly List<VideoBackdropService> _services = [];

    public VideoBackdropServiceTests()
    {
        Directory.CreateDirectory(_root);
        _options.CurrentValue.Returns(new HlsMediaStreamService.ServiceOptions { BaseAddress = "http://host:5251/" });
        _encoder.EncodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => WriteAsync(call.ArgAt<string>(1)));
    }

    private VideoBackdropService Service()
    {
        var service = new VideoBackdropService(NullLogger<VideoBackdropService>.Instance, _options, _probe, _encoder, _root);
        _services.Add(service);
        return service;
    }

    private Media Video(string name = "waves.mkv", VideoBackdropFacts? facts = null, MediaType type = MediaType.Video)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "picture");
        _probe.ProbeAsync(path, Arg.Any<CancellationToken>()).Returns(facts ?? NeedsEncoding);

        return new Media { Title = name, FilePath = path, Type = type };
    }

    private static string TokenOf(string url) => url[(url.LastIndexOf('/') + 1)..];

    private static async Task<bool> WriteAsync(string path)
    {
        await File.WriteAllTextAsync(path, "encoded");
        return true;
    }

    [Theory]
    [InlineData("mov,mp4,m4a,3gp,3g2,mj2", "h264", "yuv420p", "aac", true)]
    [InlineData("mov,mp4,m4a,3gp,3g2,mj2", "h264", "yuv420p", null, true)]
    [InlineData("mov,mp4,m4a,3gp,3g2,mj2", "h264", "yuvj420p", "aac", true)]
    [InlineData("mov,mp4,m4a,3gp,3g2,mj2", "hevc", "yuv420p", "aac", false)]
    [InlineData("mov,mp4,m4a,3gp,3g2,mj2", "h264", "yuv420p", "mp3", false)]
    [InlineData("matroska,webm", "h264", "yuv420p", "aac", false)]
    [InlineData("mov,mp4,m4a,3gp,3g2,mj2", "h264", "yuv420p10le", "aac", false)]
    [InlineData("mov,mp4,m4a,3gp,3g2,mj2", "h264", "yuv422p", "aac", false)]
    [InlineData("mov,mp4,m4a,3gp,3g2,mj2", "h264", "yuv444p", "aac", false)]
    [InlineData("mov,mp4,m4a,3gp,3g2,mj2", "h264", null, "aac", false)]
    public void PlaysAsIs_OnlyEightBit420H264InMp4WithAacOrNoSound(string format, string video, string? pixels, string? audio, bool expected)
        => Assert.Equal(expected, VideoBackdropService.PlaysAsIs(new VideoBackdropFacts(format, video, pixels, audio)));

    [Fact]
    public async Task UrlForAsync_TenBitH264InMp4_ServesAnEncode()
    {
        var media = Video("waves.mp4", Direct with { PixelFormat = "yuv420p10le" });
        var service = Service();

        var url = await service.UrlForAsync(media);

        Assert.NotEqual(media.FilePath, service.ResolveFile(TokenOf(url!)));
        await _encoder.Received(1).EncodeAsync(media.FilePath, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UrlForAsync_AVideoTheScreenPlays_ServesTheLibraryFileWithoutEncoding()
    {
        var media = Video("waves.mp4", Direct);
        var service = Service();

        var url = await service.UrlForAsync(media);

        Assert.StartsWith("http://host:5251/media/backdrops/", url);
        Assert.Equal(media.FilePath, service.ResolveFile(TokenOf(url!)));
        Assert.Empty(_encoder.ReceivedCalls());
    }

    [Fact]
    public async Task UrlForAsync_AVideoTheScreenCannotPlay_ServesAnEncode()
    {
        var media = Video();
        var service = Service();

        var url = await service.UrlForAsync(media);

        var served = service.ResolveFile(TokenOf(url!));
        Assert.NotNull(served);
        Assert.Equal(Path.Combine(service.WorkingDirectory, Path.GetFileName(served)), served);
        Assert.Equal(".mp4", Path.GetExtension(served));
        Assert.Equal("encoded", await File.ReadAllTextAsync(served));
        await _encoder.Received(1).EncodeAsync(media.FilePath, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UrlForAsync_TwoAsksAtOnce_ShareOneEncode()
    {
        var media = Video();
        var gate = new TaskCompletionSource();
        _encoder.EncodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async call => { await gate.Task; return await WriteAsync(call.ArgAt<string>(1)); });
        var service = Service();

        var first = service.UrlForAsync(media);
        var second = service.UrlForAsync(media);
        gate.SetResult();

        Assert.Equal(await first, await second);
        await _encoder.Received(1).EncodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UrlForAsync_TheSameFileAgain_ReusesTheEncodeButProbesAgain()
    {
        var media = Video();
        var service = Service();

        var first = await service.UrlForAsync(media);
        var second = await service.UrlForAsync(media);

        Assert.Equal(first, second);
        await _encoder.Received(1).EncodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _probe.Received(2).ProbeAsync(media.FilePath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UrlForAsync_AFileWrittenSince_EncodesItAgain()
    {
        var media = Video();
        var service = Service();
        var first = await service.UrlForAsync(media);

        File.SetLastWriteTimeUtc(media.FilePath, File.GetLastWriteTimeUtc(media.FilePath).AddMinutes(1));
        var second = await service.UrlForAsync(media);

        Assert.NotEqual(first, second);
        await _encoder.Received(2).EncodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UrlForAsync_TheEncodeFails_AnswersNullAndTriesAgainNextTime()
    {
        var media = Video();
        _encoder.EncodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var service = Service();

        Assert.Null(await service.UrlForAsync(media));
        Assert.Null(await service.UrlForAsync(media));

        await _encoder.Received(2).EncodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Empty(Directory.Exists(service.WorkingDirectory) ? Directory.GetFiles(service.WorkingDirectory, "*.mp4*") : []);
    }

    [Fact]
    public async Task UrlForAsync_AMissingFile_AnswersNullWithoutProbing()
    {
        var media = new Media { Title = "Gone", FilePath = Path.Combine(_root, "gone.mp4"), Type = MediaType.Video };

        Assert.Null(await Service().UrlForAsync(media));
        Assert.Empty(_probe.ReceivedCalls());
    }

    [Fact]
    public async Task UrlForAsync_ARowThatIsNotAVideo_AnswersNull()
        => Assert.Null(await Service().UrlForAsync(Video("song.mp4", Direct, MediaType.Karaoke)));

    [Fact]
    public async Task UrlForAsync_AFileTheProbeCannotRead_AnswersNull()
    {
        var media = Video();
        _probe.ProbeAsync(media.FilePath, Arg.Any<CancellationToken>()).Returns((VideoBackdropFacts?)null);

        Assert.Null(await Service().UrlForAsync(media));
        Assert.Empty(_encoder.ReceivedCalls());
    }

    [Fact]
    public async Task UrlForAsync_AFileWithNoPicture_AnswersNull()
    {
        var media = Video("sound.m4a", new VideoBackdropFacts("mov,mp4,m4a,3gp,3g2,mj2", null, null, "aac"));

        Assert.Null(await Service().UrlForAsync(media));
        Assert.Empty(_encoder.ReceivedCalls());
    }

    [Fact]
    public async Task DirectUrlForAsync_AVideoNeedingAnEncode_AnswersNullWithoutEncoding()
    {
        Assert.Null(await Service().DirectUrlForAsync(Video()));
        Assert.Empty(_encoder.ReceivedCalls());
    }

    [Fact]
    public async Task DirectUrlForAsync_AVideoTheScreenPlays_ServesTheLibraryFile()
    {
        var media = Video("waves.mp4", Direct);
        var service = Service();

        var url = await service.DirectUrlForAsync(media);

        Assert.Equal(media.FilePath, service.ResolveFile(TokenOf(url!)));
    }

    [Fact]
    public async Task ResolveFile_ATokenNeverHandedOut_AnswersNull()
    {
        var service = Service();
        await service.UrlForAsync(Video("waves.mp4", Direct));

        Assert.Null(service.ResolveFile(Guid.NewGuid().ToString("n")));
    }

    [Fact]
    public async Task ResolveFile_AFileSinceGone_AnswersNull()
    {
        var media = Video("waves.mp4", Direct);
        var service = Service();
        var url = await service.UrlForAsync(media);

        File.Delete(media.FilePath);

        Assert.Null(service.ResolveFile(TokenOf(url!)));
    }

    [Fact]
    public async Task Dispose_DeletesThisRunsEncodes()
    {
        var service = Service();
        await service.UrlForAsync(Video());
        Assert.True(Directory.Exists(service.WorkingDirectory));

        service.Dispose();

        Assert.False(Directory.Exists(service.WorkingDirectory));
    }

    [Fact]
    public void Constructor_SweepsAFolderWhoseOwnerIsGone_AndKeepsALiveOnes()
    {
        var orphan = Path.Combine(_root, Guid.NewGuid().ToString("n"));
        var live = Path.Combine(_root, Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(orphan);
        Directory.CreateDirectory(live);
        File.WriteAllText(Path.Combine(orphan, HlsSessionSweeper.OwnerFileName), int.MaxValue.ToString());
        HlsSessionSweeper.WriteOwner(live);

        Service();

        Assert.False(Directory.Exists(orphan));
        Assert.True(Directory.Exists(live));
    }

    [Fact]
    public void BuildArguments_PictureOnlyH264Mp4FittedInto720p()
    {
        var arguments = FfmpegVideoBackdropEncoder.BuildArguments("/in/waves.mkv", "/out/x.mp4.part");
        var line = string.Join(' ', arguments);

        Assert.Contains("-i /in/waves.mkv", line);
        Assert.Contains("-map 0:V:0", line);
        Assert.Contains("-an", arguments);
        Assert.Contains("-t 600", line);
        Assert.Contains("-vf scale=w=1280:h=720:force_original_aspect_ratio=decrease,scale=trunc(iw/2)*2:trunc(ih/2)*2", line);
        Assert.Contains("-c:v libx264 -preset veryfast -profile:v high -pix_fmt yuv420p", line);
        Assert.Contains("-movflags +faststart", line);
        Assert.EndsWith("-f mp4 /out/x.mp4.part", line);
    }

    public void Dispose()
    {
        foreach (var service in _services) service.Dispose();

        try { Directory.Delete(_root, recursive: true); }
        catch { /* swept by the OS */ }

        GC.SuppressFinalize(this);
    }
}
